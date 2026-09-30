using ScreeningLoader.Core.Audit;
using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Shell;

/// <summary>Una oferta, sin la descripción: la interfaz no la muestra y es lo más pesado de la fila.</summary>
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

/// <summary>Un archivo que entra a la corrida.</summary>
internal sealed record DetectedWire(string FileName, long SizeBytes);

/// <summary>Un archivo que el filtro dejó afuera, con el motivo ya escrito para leer.</summary>
internal sealed record ExcludedWire(string FileName, string Reason);

/// <summary>Los ajustes que la persona puede cambiar.</summary>
internal sealed record SettingsWire(
    string AppCode,
    int MaxFileSizeMb,
    string ResponseLanguage,
    int AuditLogRetentionDays,
    int BatchSize,
    int MaxBatchMb);

/// <summary>Lo que se encontró en la carpeta.</summary>
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

/// <summary>Un archivo que no se registró, y por qué.</summary>
internal sealed record FailureWire(string FileName, string Reason);

/// <summary>Una corrida anterior, tal como la lista el historial.</summary>
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
    /// Se usa sólo en las corridas registradas antes de que el título se guardara en el registro.
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

/// <summary>Un avance de la corrida, con la forma que consume la interfaz.</summary>
internal static class RunEventWire
{
    /// <summary>
    /// Traduce un evento del motor, o devuelve null si la interfaz no lo muestra.
    /// </summary>
    /// <param name="excludedByUser">
    /// Cuántos archivos dejó fuera la persona, que el resumen cuenta aparte de los del filtro.
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

        // Las tandas, los reintentos y el turno caído son mecánica de la plataforma, no del recorrido.
        _ => null
    };

    /// <summary>La corrida terminó mal, y la interfaz muestra el patrón de error.</summary>
    public static object Failed(ErrorKind kind, string message) =>
        new { type = "runFailed", kind = kind.ToString(), message };

    /// <summary>La persona detuvo la corrida.</summary>
    public static object Cancelled() => new { type = "runCancelled" };
}
