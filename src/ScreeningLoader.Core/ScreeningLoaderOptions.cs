namespace ScreeningLoader.Core;

/// <summary>
/// Configuración del motor.
/// </summary>
public sealed record ScreeningLoaderOptions
{
    public string BaseUrl { get; init; } = "https://api.serenitystar.ai";

    /// <summary>
    /// Código de la app que sirve el agente. Es lo que se configura: el agente se descubre a partir de él.
    /// </summary>
    public string AppCode { get; init; } = "candidate-screening-studio";


    public int BatchSize { get; init; } = 10;

    /// <summary>Tope de peso acumulado por lote. Provisional hasta medirlo con CVs reales.</summary>
    public long MaxBatchBytes { get; init; } = 10L * 1024 * 1024;

    public int MaxParallelBatches { get; init; } = 1;
    public int MaxParallelUploads { get; init; } = 3;

    /// <summary>
    /// Tope por archivo. Muy por debajo del techo real del Hub, que lo pone el servidor web cerca de los 30 MB.
    /// </summary>
    public int MaxFileSizeMb { get; init; } = 20;
    public bool ProcessEmbeddings { get; init; }
    public int UploadPollIntervalMs { get; init; } = 1_000;
    public int UploadPollTimeoutMs { get; init; } = 120_000;
    public int RequestTimeoutMs { get; init; } = 100_000;

    /// <summary>Tope de un turno de análisis. Un lote de diez CVs tarda minutos.</summary>
    public int AnalyzeTimeoutMs { get; init; } = 900_000;

    public string ResponseLanguage { get; init; } = "Spanish";
    public int RetryMaxAttempts { get; init; } = 4;
    public int RetryBaseDelayMs { get; init; } = 2_000;
    public int AuditLogRetentionDays { get; init; } = 90;


    public string ProcessedFolderName { get; init; } = "procesados";
    public string ReviewFolderName { get; init; } = "fallidos";
}
