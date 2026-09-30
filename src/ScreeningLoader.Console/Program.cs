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

// The history comes from the records on disk, so it needs no session.
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

    Console.Write("Password: ");
    string password = ReadPasswordMasked();

    await engine.LoginAsync(email, password, cancellation.Token);
    await engine.ResolveAgentAsync(cancellation.Token);

    string datasetSkillCode = await engine.GetDatasetSkillCodeAsync(cancellation.Token);
    Console.WriteLine($"Agent: {engine.AgentCode}. Dataset skill: {datasetSkillCode}");

    if (args.Contains(ProbeRefreshFlag))
    {
        await engine.RefreshSessionAsync(cancellation.Token);

        string afterRefresh = await engine.GetDatasetSkillCodeAsync(cancellation.Token);
        Console.WriteLine($"Token refreshed. Dataset skill: {afterRefresh}");
    }

    IReadOnlyList<JobOpening> openings = await engine.ListJobOpeningsAsync(cancellation.Token);

    if (openings.Count == 0)
    {
        Console.WriteLine("The organization has no job openings loaded.");
        return 0;
    }

    Console.WriteLine();

    foreach ((JobOpening opening, int number) in openings.Select((o, i) => (o, i + 1)))
        Console.WriteLine($"[{number}] {DescribeOpening(opening)}");

    JobOpening selected = ChooseOpening(openings);

    // The description is kept whole: it is the only thing the agent scores against.
    Console.WriteLine(
        $"Chosen: {selected.Title} · {selected.CandidateCount} candidates · "
        + $"JD of {selected.Description.Length} characters");

    Console.Write("CV folder > ");
    string folder = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

    DiscoveryResult discovery = await engine.DiscoverAsync(folder, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"{discovery.Accepted.Count} accepted, {discovery.Rejected.Count} rejected");

    foreach (RejectedFile rejected in discovery.Rejected)
        Console.WriteLine($"  - {rejected.FileName}: {DescribeRejection(rejected.Reason)}");

    foreach ((Batch batch, int number) in discovery.Batches.Select((b, i) => (b, i + 1)))
        Console.WriteLine($"  batch {number}: {batch.Files.Count} files, {batch.TotalMegabytes:F1} MB");

    // Uploading and analyzing leave data in the Hub and rows in the dataset, so they only run when asked.
    if (discovery.Batches.Count == 0)
        return 0;

    Batch first = discovery.Batches[0];

    if (args.Contains(AnalyzeFlag))
    {
        Console.WriteLine();

        await foreach (RunEvent progress in engine.RunAsync(selected, discovery, folder, cancellation.Token))
            Report(progress);

        Console.WriteLine();
        Console.WriteLine($"Results: {engine.ResultsUrl}");

        return 0;
    }

    if (!args.Contains(UploadFlag))
        return 0;

    Console.WriteLine();
    Console.WriteLine($"Uploading batch 1 · {first.Files.Count} files");

    UploadOutcome outcome = await engine.UploadBatchAsync(first, Report, cancellation.Token);

    Console.WriteLine();
    Console.WriteLine($"{outcome.Ready.Count} ready, {outcome.Failed.Count} failed");

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
    Console.Error.WriteLine("Cancelled.");
    return 1;
}

static void Report(RunEvent runEvent)
{
    switch (runEvent)
    {
        case FileUploading uploading:
            Console.WriteLine($"  uploading {uploading.FileName} · {uploading.SizeBytes / 1024.0:F0} KB");
            break;

        case FileProcessing processing:
            Console.WriteLine($"  processing {processing.FileName}");
            break;

        case FileUploaded uploaded:
            Console.WriteLine($"  ready {uploaded.FileName}");
            break;

        case FileFailed failed:
            Console.WriteLine($"  failed {failed.FileName}: {failed.Reason}");
            break;

        case FileRegistered registered:
            Console.WriteLine($"  registered {registered.FileName}");
            break;

        case BatchAnalyzing analyzing:
            Console.WriteLine($"  analyzing {analyzing.FileCount} CVs in one turn");
            break;

        case BatchAnalyzeFailed failure:
            Console.WriteLine($"  the turn failed: {failure.Reason}");
            Console.WriteLine("  reconciling against the dataset whatever it managed to write");
            break;

        case AgentProgress progress:
            Console.WriteLine($"  {progress.EstimatedProgress?.ToString() ?? "--"}% · {progress.Title}");
            break;

        case RunStarted started:
            Console.WriteLine($"Run: {started.FileCount} CVs in {started.BatchCount} batches");
            break;

        case BatchStarted batch:
            Console.WriteLine();
            Console.WriteLine($"Batch {batch.Number} of {batch.Of} · {batch.FileCount} files");
            break;

        case RetryWaiting waiting:
            Console.WriteLine(
                $"  retry {waiting.Attempt} of {waiting.Operation} in {waiting.Delay.TotalSeconds:F0}s "
                + $"· {waiting.Reason}");
            break;

        case RunCompleted completed:
            Console.WriteLine();
            Console.WriteLine(
                $"{completed.Totals.Processed} processed · {completed.Totals.ToReview} in fallidos/ · "
                + $"{completed.Totals.Rejected} rejected"
                + (completed.Totals.FailedMoves > 0 ? $" · {completed.Totals.FailedMoves} not moved" : ""));
            break;
    }
}

static string Describe(Guid? fileId) => fileId is { } id ? id.ToString() : "no download id";

static string DescribeOpening(JobOpening opening)
{
    string department = opening.Department.Length > 0 ? opening.Department : "no department";
    string candidates = opening.CandidateCount == 1 ? "1 candidate" : $"{opening.CandidateCount} candidates";

    return $"{opening.Title} · {department} · {opening.Status} · {candidates}";
}

static string DescribeRejection(RejectionReason reason) => reason switch
{
    RejectionReason.UnsupportedType => "the agent does not accept this file type",
    RejectionReason.Empty => "is empty",
    RejectionReason.TooLarge => "exceeds the maximum size per file",
    RejectionReason.ExceedsBatchBudget => "does not fit in a batch on its own",
    _ => reason.ToString()
};

static JobOpening ChooseOpening(IReadOnlyList<JobOpening> openings)
{
    while (true)
    {
        Console.Write($"Opening [1-{openings.Count}] > ");

        if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= openings.Count)
            return openings[choice - 1];

        Console.WriteLine("Not an option from the list.");
    }
}

static string ReadPasswordMasked()
{
    // With redirected input there are no keys to intercept, and the host has to stay scriptable.
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
        ? $"{t.Processed} processed · {t.ToReview} failed · {t.Rejected} rejected"
        : "unfinished";

    string detail = string.Concat(
        run.ToReview.Select(f => $"{Environment.NewLine}    - {f.FileName}: {f.Reason}"));

    string opening = run.OpeningTitle.Length > 0 ? run.OpeningTitle : run.OpeningId[..8];

    return $"{run.Id}  {run.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {opening}  {totals}{detail}";
}
