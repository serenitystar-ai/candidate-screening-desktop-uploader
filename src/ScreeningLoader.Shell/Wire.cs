using ScreeningLoader.Core.Audit;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Shell;

/// <summary>A job opening, without the description: the interface does not show it and it is the heaviest part of the row.</summary>
internal sealed record OpeningWire(
    string Id,
    string Title,
    string Department,
    string Location,
    string EmploymentType,
    string Status,
    int CandidateCount)
{
    public static OpeningWire From(JobOpening opening) => new(
        opening.Id,
        opening.Title,
        opening.Department,
        opening.Location,
        opening.EmploymentType,
        opening.Status,
        opening.CandidateCount);
}

/// <summary>A file that goes into the run.</summary>
internal sealed record DetectedWire(string FileName, long SizeBytes);

/// <summary>A file the filter left out, with the reason already written to be read.</summary>
internal sealed record ExcludedWire(string FileName, string Reason);

/// <summary>The settings the person can change.</summary>
internal sealed record SettingsWire(
    string AppCode,
    int MaxFileSizeMb,
    string ResponseLanguage,
    int AuditLogRetentionDays,
    int BatchSize,
    int MaxBatchMb);

/// <summary>What was found in the folder.</summary>
internal sealed record DiscoveryWire(
    IReadOnlyList<DetectedWire> Detected,
    IReadOnlyList<ExcludedWire> Excluded,
    IReadOnlyList<DetectedWire> Failed)
{
    public static DiscoveryWire From(DiscoveryResult discovery) => new(
        [.. discovery.Accepted.Select(file => new DetectedWire(file.FileName, file.SizeBytes))],
        [.. discovery.Rejected.Select(file => new ExcludedWire(file.FileName, Describe(file.Reason)))],
        [.. discovery.Failed.Select(file => new DetectedWire(file.FileName, file.SizeBytes))]);

    private static string Describe(RejectionReason reason) => reason switch
    {
        RejectionReason.UnsupportedType => "Formato no admitido",
        RejectionReason.Empty => "El archivo está vacío",
        RejectionReason.TooLarge => "Supera el tamaño máximo",
        RejectionReason.ExceedsBatchBudget => "Demasiado grande para procesarlo",
        RejectionReason.ExcludedByUser => "Exclusión manual",
        _ => "No se puede procesar"
    };
}

/// <summary>A file that was not registered, and why.</summary>
internal sealed record FailureWire(string FileName, string Reason);

/// <summary>A previous run, as the history lists it.</summary>
internal sealed record RunWire(
    string Id,
    DateTimeOffset StartedAt,
    string OpeningTitle,
    bool Completed,
    int Processed,
    int ToReview,
    int Rejected,
    IReadOnlyList<FailureWire> Failures)
{
    /// <param name="fallbackTitle">
    /// Used only for runs recorded before the title was saved in the record.
    /// </param>
    public static RunWire From(RunRecord run, string fallbackTitle) => new(
        run.Id,
        run.StartedAt,
        run.OpeningTitle.Length > 0 ? run.OpeningTitle : fallbackTitle,
        run.Totals is not null,
        run.Totals?.Processed ?? 0,
        run.Totals?.ToReview ?? run.ToReview.Count,
        run.Totals?.Rejected ?? 0,
        [.. run.ToReview.Select(failure => new FailureWire(failure.FileName, failure.Reason))]);
}

/// <summary>A run progress update, in the shape the interface consumes.</summary>
internal static class RunEventWire
{
    /// <summary>
    /// Translates an engine event, or returns null if the interface does not show it.
    /// </summary>
    /// <param name="excludedByUser">
    /// How many files the person left out, which the summary counts separately from the filter's.
    /// </param>
    public static object? From(RunEvent progress, int excludedByUser) => progress switch
    {
        RunStarted e => new { type = "runStarted", fileCount = e.FileCount },
        FileUploading e => new { type = "fileUploading", fileName = e.FileName },
        FileProcessing e => new { type = "fileReading", fileName = e.FileName },
        FileUploaded e => new { type = "fileReady", fileName = e.FileName },
        FileRegistered e => new { type = "fileRegistered", fileName = e.FileName },
        FileFailed e => new { type = "fileFailed", fileName = e.FileName, reason = e.Reason },
        BatchAnalyzing e => new { type = "analyzing", fileCount = e.FileCount },
        AgentProgress e => new { type = "agentStep", title = e.Title, percent = e.EstimatedProgress },
        RunCompleted e => new
        {
            type = "runCompleted",
            processed = e.Totals.Processed,
            toReview = e.Totals.ToReview,
            rejected = e.Totals.Rejected - excludedByUser,
            excludedByUser,
            failedMoves = e.Totals.FailedMoves
        },

        // Batches, retries and the failed turn are platform mechanics, not part of the journey.
        _ => null
    };

    /// <summary>The run ended badly, and the interface shows the error pattern.</summary>
    public static object Failed(ErrorKind kind, string message) =>
        new { type = "runFailed", kind = kind.ToString(), message };

    /// <summary>The person stopped the run.</summary>
    public static object Cancelled() => new { type = "runCancelled" };
}
