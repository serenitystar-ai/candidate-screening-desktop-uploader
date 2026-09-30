using System.Text;
using ScreeningLoader.Core;
using ScreeningLoader.Core.Audit;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

const string ProbeRefreshFlag = "--probe-refresh";
const string UploadFlag = "--upload";
const string AnalyzeFlag = "--analyze";
const string HistoryFlag = "--history";

ScreeningLoaderOptions options = new();
ErrorLog errorLog = new(ErrorLog.DefaultDirectory);

using CancellationTokenSource cancellation = new();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

using ScreeningLoaderEngine engine = new(options, errorLog);

// El historial sale de los registros en disco, así que no necesita sesión.
if (args.Contains(HistoryFlag))
{
    foreach (RunRecord run in engine.ListRuns(20))
        Console.WriteLine(DescribeRun(run));

    return 0;
}

try
{
    Console.Write("Email: ");
    string email = Console.ReadLine() ?? string.Empty;

    Console.Write("Contraseña: ");
    string password = ReadPasswordMasked();

    await engine.LoginAsync(email, password, cancellation.Token);
    await engine.ResolveAgentAsync(cancellation.Token);

    string datasetSkillCode = await engine.GetDatasetSkillCodeAsync(cancellation.Token);
    Console.WriteLine($"Agente: {engine.AgentCode}. Dataset skill: {datasetSkillCode}");

    if (args.Contains(ProbeRefreshFlag))
    {
        await engine.RefreshSessionAsync(cancellation.Token);

        string afterRefresh = await engine.GetDatasetSkillCodeAsync(cancellation.Token);
        Console.WriteLine($"Token renovado. Dataset skill: {afterRefresh}");
    }

    IReadOnlyList<JobOpening> openings = await engine.ListJobOpeningsAsync(cancellation.Token);

    if (openings.Count == 0)
    {
        Console.WriteLine("La organización no tiene búsquedas laborales cargadas.");
        return 0;
    }

    Console.WriteLine();

    foreach ((JobOpening opening, int number) in openings.Select((o, i) => (o, i + 1)))
        Console.WriteLine($"[{number}] {DescribeOpening(opening)}");

    JobOpening selected = ChooseOpening(openings);

    // La descripción se retiene entera: es lo único con lo que el agente puntúa.
    Console.WriteLine(
        $"Elegido: {selected.Title} · {selected.CandidateCount} candidatos · "
        + $"JD de {selected.Description.Length} caracteres");

    Console.Write("Carpeta de CVs > ");
    string folder = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

    DiscoveryResult discovery = await engine.DiscoverAsync(folder, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"{discovery.Accepted.Count} aceptados, {discovery.Rejected.Count} descartados");

    foreach (RejectedFile rejected in discovery.Rejected)
        Console.WriteLine($"  - {rejected.FileName}: {DescribeRejection(rejected.Reason)}");

    foreach ((Batch batch, int number) in discovery.Batches.Select((b, i) => (b, i + 1)))
        Console.WriteLine($"  lote {number}: {batch.Files.Count} archivos, {batch.TotalMegabytes:F1} MB");

    // Subir y analizar dejan datos en el Hub y filas en el dataset, así que no corren sin pedirlo.
    if (discovery.Batches.Count == 0)
        return 0;

    Batch first = discovery.Batches[0];

    if (args.Contains(AnalyzeFlag))
    {
        Console.WriteLine();

        await foreach (RunEvent progress in engine.RunAsync(selected, discovery, folder, cancellation.Token))
            Report(progress);

        Console.WriteLine();
        Console.WriteLine($"Resultados: {engine.ResultsUrl}");

        return 0;
    }

    if (!args.Contains(UploadFlag))
        return 0;

    Console.WriteLine();
    Console.WriteLine($"Subiendo el lote 1 · {first.Files.Count} archivos");

    UploadOutcome outcome = await engine.UploadBatchAsync(first, Report, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"{outcome.Ready.Count} listos, {outcome.Failed.Count} fallidos");

    foreach (UploadedCv cv in outcome.Ready)
        Console.WriteLine($"  {cv.File.FileName} · vk={cv.VolatileKnowledgeId} · file={Describe(cv.FileId)}");

    foreach (FailedCv failed in outcome.Failed)
        Console.WriteLine($"  - {failed.FileName}: {failed.Reason}");

    return 0;
}
catch (ScreeningLoaderException ex)
{
    Console.Error.WriteLine($"[{ex.Kind}] {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelado.");
    return 1;
}

static void Report(RunEvent runEvent)
{
    switch (runEvent)
    {
        case FileUploading uploading:
            Console.WriteLine($"  subiendo {uploading.FileName} · {uploading.SizeBytes / 1024.0:F0} KB");
            break;

        case FileProcessing processing:
            Console.WriteLine($"  procesando {processing.FileName}");
            break;

        case FileUploaded uploaded:
            Console.WriteLine($"  listo {uploaded.FileName}");
            break;

        case FileFailed failed:
            Console.WriteLine($"  falló {failed.FileName}: {failed.Reason}");
            break;

        case FileRegistered registered:
            Console.WriteLine($"  registrado {registered.FileName}");
            break;

        case BatchAnalyzing analyzing:
            Console.WriteLine($"  analizando {analyzing.FileCount} CVs en un turno");
            break;

        case BatchAnalyzeFailed failure:
            Console.WriteLine($"  el turno falló: {failure.Reason}");
            Console.WriteLine("  reconciliando contra el dataset lo que haya alcanzado a escribir");
            break;

        case AgentProgress progress:
            Console.WriteLine($"  {progress.EstimatedProgress?.ToString() ?? "--"}% · {progress.Title}");
            break;

        case RunStarted started:
            Console.WriteLine($"Corrida: {started.FileCount} CVs en {started.BatchCount} lotes");
            break;

        case BatchStarted batch:
            Console.WriteLine();
            Console.WriteLine($"Lote {batch.Number} de {batch.Of} · {batch.FileCount} archivos");
            break;

        case RetryWaiting waiting:
            Console.WriteLine(
                $"  reintento {waiting.Attempt} de {waiting.Operation} en {waiting.Delay.TotalSeconds:F0}s "
                + $"· {waiting.Reason}");
            break;

        case RunCompleted completed:
            Console.WriteLine();
            Console.WriteLine(
                $"{completed.Totals.Processed} procesados · {completed.Totals.ToReview} en fallidos/ · "
                + $"{completed.Totals.Rejected} descartados"
                + (completed.Totals.FailedMoves > 0 ? $" · {completed.Totals.FailedMoves} sin mover" : ""));
            break;
    }
}

static string Describe(Guid? fileId) => fileId is { } id ? id.ToString() : "sin id de descarga";

static string DescribeOpening(JobOpening opening)
{
    string department = opening.Department.Length > 0 ? opening.Department : "sin área";
    string candidates = opening.CandidateCount == 1 ? "1 candidato" : $"{opening.CandidateCount} candidatos";

    return $"{opening.Title} · {department} · {opening.Status} · {candidates}";
}

static string DescribeRejection(RejectionReason reason) => reason switch
{
    RejectionReason.UnsupportedType => "el agente no acepta ese tipo de archivo",
    RejectionReason.Empty => "está vacío",
    RejectionReason.TooLarge => "supera el tamaño máximo por archivo",
    RejectionReason.ExceedsBatchBudget => "por sí solo no entra en un lote",
    _ => reason.ToString()
};

static JobOpening ChooseOpening(IReadOnlyList<JobOpening> openings)
{
    while (true)
    {
        Console.Write($"Opening [1-{openings.Count}] > ");

        if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= openings.Count)
            return openings[choice - 1];

        Console.WriteLine("No es una opción de la lista.");
    }
}

static string ReadPasswordMasked()
{
    // Con la entrada redirigida no hay teclas que interceptar, y el host tiene que seguir siendo scripteable.
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? string.Empty;

    StringBuilder password = new();

    while (true)
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                Console.WriteLine();
                return password.ToString();

            case ConsoleKey.Backspace when password.Length > 0:
                password.Length--;
                Console.Write("\b \b");
                break;

            default:
                if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write('*');
                }

                break;
        }
    }
}

static string DescribeRun(RunRecord run)
{
    string totals = run.Totals is { } t
        ? $"{t.Processed} procesados · {t.ToReview} fallidos · {t.Rejected} descartados"
        : "sin terminar";

    string detail = string.Concat(
        run.ToReview.Select(f => $"{Environment.NewLine}    - {f.FileName}: {f.Reason}"));

    string opening = run.OpeningTitle.Length > 0 ? run.OpeningTitle : run.OpeningId[..8];

    return $"{run.Id}  {run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {opening}  {totals}{detail}";
}
