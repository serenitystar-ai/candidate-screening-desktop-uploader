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
/// State machine of a run.
/// </summary>
public sealed class BatchRunner(
    ISerenityClient serenityClient,
    IScreeningClient screeningClient,
    TokenStore tokenStore,
    RetryPolicy retryPolicy,
    ScreeningLoaderOptions options)
{
    /// <summary>
    /// Processes every batch of a run and emits its progress.
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
            // Before the batch and not on a 401: a token that expires halfway through a multi-minute turn
            // throws away work already paid for.
            await tokenStore.GetAccessTokenAsync(ct);

            yield return new BatchStarted(number, discovery.Batches.Count, batch.Files.Count);

            // Batches run one after another: the dataset is SQLite and allows a single writer.
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
    /// Processes a whole batch: uploads, analyzes, reconciles against the dataset and sorts the files.
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
                // The batch is retried with the same ids: they expire by time, not by use, so a 429
                // on the execution does not cost re-uploading anything.
                receipt = await retryPolicy.ExecuteAsync(
                    "analyze",
                    token => serenityClient.AnalyzeAsync(opening, uploaded.Ready, emit, token),
                    emit,
                    ct);

                turnCompleted = true;
            }
            catch (ScreeningLoaderException ex) when (ex.Kind is not ErrorKind.Fatal)
            {
                // A turn that dies halfway may have written real rows, and those rows are
                // candidates. It is reconciled anyway: discarding them would archive wrongly and duplicate later.
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

        // A CV the agent rejected, or whose row the dataset does not confirm, is only known here. Without this
        // notice the host only sees the total and the user never learns what dropped out or why.
        HashSet<string> alreadyReported = [.. uploaded.Failed.Select(f => f.FileName)];

        foreach (FailedCv failure in outcome.ToReview.Where(f => !alreadyReported.Contains(f.FileName)))
            emit(new FileFailed(failure.FileName, failure.Reason));

        ArchiveResult archive = new FileArchiver(options).Archive(outcome, folder);

        // An uploaded file is not yet a candidate: only once the row is confirmed and the file moved
        // can the host consider it done.
        foreach (CvFile file in outcome.Processed)
            emit(new FileRegistered(file.FileName));

        return new BatchResult(outcome, archive);
    }

    /// <summary>
    /// Uploads a batch's files with bounded parallelism and returns the ones that ended up ready.
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

        // The batch order is the folder's; concurrent uploads lose it and it has to be restored.
        Dictionary<string, int> position = batch.Files
            .Select((file, index) => (file.Path, index))
            .ToDictionary(entry => entry.Path, entry => entry.index);

        return new UploadOutcome(
            [.. ready.OrderBy(cv => position[cv.File.Path])],
            [.. failed.OrderBy(cv => cv.FileName, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Runs a batch, pushing its events into a stream the host consumes.
    /// </summary>
    private async IAsyncEnumerable<RunEvent> RunBatchAsync(
        JobOpening opening,
        Batch batch,
        int number,
        string sourceFolder,
        AuditLog audit,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Uploads and the turn report through a callback and the host consumes a stream: the channel is the seam.
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
    /// The file ↔ row mapping, which comes from the dataset and never from counting receipt entries.
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
                "reconcile",
                token => screeningClient.GetInsertedAsync(receipt.InsertedIds, token),
                emit,
                ct));
        }

        // Filled in by name in two cases: when the turn never acknowledged, and when it acknowledged with some
        // id that was not a GUID. No written row may go unconfirmed because of a badly written receipt.
        bool incomplete = !turnCompleted || receipt.UnreadableIds > 0;

        if (!incomplete || uploaded.Ready.Count == 0)
            return confirmed;

        // Less precise than by id —two runs with the same name on the same job opening get mixed up—
        // but limited to this batch, and it is the only thing available.
        IReadOnlyList<InsertedCandidate> byName = await retryPolicy.ExecuteAsync(
            "reconcile by name",
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
    /// Uploads run in parallel, so the host should not have to synchronize its own emit.
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
