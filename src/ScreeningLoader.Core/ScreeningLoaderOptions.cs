namespace ScreeningLoader.Core;

/// <summary>
/// Engine configuration.
/// </summary>
public sealed record ScreeningLoaderOptions
{
    public string BaseUrl { get; init; } = "https://api.serenitystar.ai";

    /// <summary>
    /// Code of the app that serves the agent. This is what gets configured: the agent is discovered from it.
    /// </summary>
    public string AppCode { get; init; } = "candidate-screening-studio";


    public int BatchSize { get; init; } = 10;

    /// <summary>Cap on combined size per batch. Provisional until measured with real CVs.</summary>
    public long MaxBatchBytes { get; init; } = 10L * 1024 * 1024;

    public int MaxParallelBatches { get; init; } = 1;
    public int MaxParallelUploads { get; init; } = 3;

    /// <summary>
    /// Per-file cap. Well below the Hub's real ceiling, which the web server sets at around 30 MB.
    /// </summary>
    public int MaxFileSizeMb { get; init; } = 20;
    public bool ProcessEmbeddings { get; init; }
    public int UploadPollIntervalMs { get; init; } = 1_000;
    public int UploadPollTimeoutMs { get; init; } = 120_000;
    public int RequestTimeoutMs { get; init; } = 100_000;

    /// <summary>Cap on an analysis turn. A batch of ten CVs takes minutes.</summary>
    public int AnalyzeTimeoutMs { get; init; } = 900_000;

    public string ResponseLanguage { get; init; } = "Spanish";
    public int RetryMaxAttempts { get; init; } = 4;
    public int RetryBaseDelayMs { get; init; } = 2_000;
    public int AuditLogRetentionDays { get; init; } = 90;


    public string ProcessedFolderName { get; init; } = "procesados";
    public string ReviewFolderName { get; init; } = "fallidos";
}
