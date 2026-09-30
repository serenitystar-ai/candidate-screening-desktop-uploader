using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ScreeningLoader.Core.Audit;
using ScreeningLoader.Core.Auth;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Screening;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Máquina de estados de una corrida.
/// </summary>
public sealed class BatchRunner(
    ISerenityClient serenityClient,
    IScreeningClient screeningClient,
    TokenStore tokenStore,
    RetryPolicy retryPolicy,
    ScreeningLoaderOptions options)
{
    /// <summary>
    /// Procesa todos los lotes de una corrida y emite su progreso.
    /// </summary>
    public async IAsyncEnumerable<RunEvent> RunAsync(
        JobOpening opening,
        DiscoveryResult discovery,
        string sourceFolder,
        AuditLog audit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (RejectedFile rejected in discovery.Rejected)
            audit.Rejected(rejected.FileName, rejected.Reason);

        yield return new RunStarted(discovery.Batches.Count, discovery.Accepted.Count);

        RunTotals totals = new(0, 0, discovery.Rejected.Count, 0);

        foreach ((Batch batch, int number) in discovery.Batches.Select((b, i) => (b, i + 1)))
        {
            // Antes del lote y no ante un 401: un token que vence en mitad de un turno de varios minutos
            // tira abajo trabajo ya pagado.
            await tokenStore.GetAccessTokenAsync(ct);

            yield return new BatchStarted(number, discovery.Batches.Count, batch.Files.Count);

            // Los lotes van uno detrás de otro: el dataset es SQLite y admite un solo escritor.
            await foreach (RunEvent progress in RunBatchAsync(opening, batch, number, sourceFolder, audit, ct))
            {
                if (progress is BatchFinished finished)
                {
                    totals = totals with
                    {
                        Processed = totals.Processed + finished.Result.Outcome.Processed.Count,
                        ToReview = totals.ToReview + finished.Result.Outcome.ToReview.Count,
                        FailedMoves = totals.FailedMoves + finished.Result.Archive.FailedMoves.Count
                    };

                    continue;
                }

                yield return progress;
            }
        }

        audit.Completed(totals);

        yield return new RunCompleted(totals);
    }

    /// <summary>
    /// Procesa un lote entero: sube, analiza, reconcilia contra el dataset y reparte los archivos.
    /// </summary>
    public async Task<BatchResult> RunBatchAsync(
        JobOpening opening,
        Batch batch,
        string folder,
        Action<RunEvent> emit,
        CancellationToken ct)
    {
        UploadOutcome uploaded = await UploadBatchAsync(batch, emit, ct);

        AnalyzeReceipt receipt = AnalyzeReceipt.Empty;
        bool turnCompleted = false;

        if (uploaded.Ready.Count > 0)
        {
            emit(new BatchAnalyzing(uploaded.Ready.Count));

            try
            {
                // El lote se reintenta con los mismos ids: expiran por tiempo, no por uso, así que un 429
                // en la ejecución no cuesta volver a subir nada.
                receipt = await retryPolicy.ExecuteAsync(
                    "analyze",
                    token => serenityClient.AnalyzeAsync(opening, uploaded.Ready, emit, token),
                    emit,
                    ct);

                turnCompleted = true;
            }
            catch (ScreeningLoaderException ex) when (ex.Kind is not ErrorKind.Fatal)
            {
                // Un turno que muere a la mitad puede haber escrito filas reales, y esas filas son
                // candidatos. Se reconcilia igual: descartarlas archivaría mal y duplicaría después.
                emit(new BatchAnalyzeFailed(ex.Message));
            }
        }

        IReadOnlyList<InsertedCandidate> inserted = await ResolveInsertedAsync(
            opening,
            uploaded,
            receipt,
            turnCompleted,
            emit,
            ct);

        BatchOutcome outcome = Reconciler.Reconcile(
            batch,
            inserted,
            [.. receipt.Failed, .. uploaded.Failed]);

        // Un CV que el agente rechazó, o cuya fila el dataset no confirma, recién se sabe acá. Sin este
        // aviso el host sólo ve el total y la persona no se entera de qué se cayó ni por qué.
        HashSet<string> alreadyReported = [.. uploaded.Failed.Select(f => f.FileName)];

        foreach (FailedCv failure in outcome.ToReview.Where(f => !alreadyReported.Contains(f.FileName)))
            emit(new FileFailed(failure.FileName, failure.Reason));

        ArchiveResult archive = new FileArchiver(options).Archive(outcome, folder);

        // Un archivo subido todavía no es un candidato: recién con la fila confirmada y el archivo movido
        // el host puede darlo por cerrado.
        foreach (CvFile file in outcome.Processed)
            emit(new FileRegistered(file.FileName));

        return new BatchResult(outcome, archive);
    }

    /// <summary>
    /// Sube en paralelo acotado los archivos de un lote y devuelve los que quedaron listos.
    /// </summary>
    public async Task<UploadOutcome> UploadBatchAsync(Batch batch, Action<RunEvent> emit, CancellationToken ct)
    {
        ConcurrentBag<UploadedCv> ready = [];
        ConcurrentBag<FailedCv> failed = [];

        Action<RunEvent> synchronized = Synchronized(emit);

        await Parallel.ForEachAsync(
            batch.Files,
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelUploads, CancellationToken = ct },
            async (file, token) =>
            {
                try
                {
                    VolatileKnowledgeRecord record = await retryPolicy.ExecuteAsync(
                        $"upload {file.FileName}",
                        attempt => serenityClient.UploadAndAwaitAsync(file, synchronized, attempt),
                        synchronized,
                        token);

                    ready.Add(UploadedCv.From(file, record));
                }
                catch (ScreeningLoaderException ex) when (ex.Kind is not ErrorKind.Fatal)
                {
                    failed.Add(new FailedCv(file.FileName, ex.Message));
                    synchronized(new FileFailed(file.FileName, ex.Message));
                }
            });

        // El orden del lote es el de la carpeta; las subidas concurrentes lo pierden y hay que recuperarlo.
        Dictionary<string, int> position = batch.Files
            .Select((file, index) => (file.Path, index))
            .ToDictionary(entry => entry.Path, entry => entry.index);

        return new UploadOutcome(
            [.. ready.OrderBy(cv => position[cv.File.Path])],
            [.. failed.OrderBy(cv => cv.FileName, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Corre un lote empujando sus eventos a un stream que el host consume.
    /// </summary>
    private async IAsyncEnumerable<RunEvent> RunBatchAsync(
        JobOpening opening,
        Batch batch,
        int number,
        string sourceFolder,
        AuditLog audit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Las subidas y el turno reportan por callback y el host consume un stream: el canal es la costura.
        Channel<RunEvent> channel = Channel.CreateUnbounded<RunEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        Task<BatchResult> work = Task.Run(
            async () =>
            {
                try
                {
                    return await RunBatchAsync(opening, batch, sourceFolder, Publish, ct);
                }
                finally
                {
                    channel.Writer.Complete();
                }
            },
            ct);

        await foreach (RunEvent progress in channel.Reader.ReadAllAsync(ct))
        {
            if (progress is RetryWaiting waiting)
                audit.Retrying(number, waiting);

            yield return progress;
        }

        BatchResult result = await work;

        foreach (CvFile file in result.Outcome.Processed)
            audit.Processed(number, file.FileName);

        foreach (FailedCv failure in result.Outcome.ToReview)
            audit.ToReview(number, failure.FileName, failure.Reason);

        foreach (FailedMove move in result.Archive.FailedMoves)
            audit.MoveFailed(number, move);

        yield return new BatchFinished(number, result);

        void Publish(RunEvent progress) => channel.Writer.TryWrite(progress);
    }

    /// <summary>
    /// El mapeo archivo ↔ fila, que sale del dataset y nunca de contar entradas del recibo.
    /// </summary>
    private async Task<IReadOnlyList<InsertedCandidate>> ResolveInsertedAsync(
        JobOpening opening,
        UploadOutcome uploaded,
        AnalyzeReceipt receipt,
        bool turnCompleted,
        Action<RunEvent> emit,
        CancellationToken ct)
    {
        List<InsertedCandidate> confirmed = [];

        if (receipt.InsertedIds.Count > 0)
        {
            confirmed.AddRange(await retryPolicy.ExecuteAsync(
                "reconciliar",
                token => screeningClient.GetInsertedAsync(receipt.InsertedIds, token),
                emit,
                ct));
        }

        // Se completa por nombre en dos casos: cuando el turno no llegó a acusar, y cuando acusó con algún
        // id que no era un GUID. Ninguna fila escrita puede quedar sin confirmar por un recibo mal escrito.
        bool incomplete = !turnCompleted || receipt.UnreadableIds > 0;

        if (!incomplete || uploaded.Ready.Count == 0)
            return confirmed;

        // Menos preciso que por id —dos corridas del mismo nombre sobre la misma búsqueda se confunden—
        // pero acotado a este lote, y es lo único disponible.
        IReadOnlyList<InsertedCandidate> byName = await retryPolicy.ExecuteAsync(
            "reconciliar por nombre",
            token => screeningClient.GetInsertedByFileNameAsync(
                opening.Id,
                [.. uploaded.Ready.Select(cv => cv.File.FileName)],
                token),
            emit,
            ct);

        HashSet<string> known = [.. confirmed.Select(c => c.CvFileName)];

        confirmed.AddRange(byName.Where(candidate => known.Add(candidate.CvFileName)));

        return confirmed;
    }

    /// <summary>
    /// Las subidas corren en paralelo, así que el host no debería tener que sincronizar su propio emit.
    /// </summary>
    private static Action<RunEvent> Synchronized(Action<RunEvent> emit)
    {
        Lock gate = new();

        return progress =>
        {
            lock (gate)
                emit(progress);
        };
    }
}
