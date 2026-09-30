using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Un CV que quedó listo para viajar a una ejecución.
/// </summary>
public sealed record UploadedCv(CvFile File, Guid VolatileKnowledgeId, Guid? FileId)
{
    internal static UploadedCv From(CvFile file, VolatileKnowledgeRecord record) =>
        new(file, record.Id, record.FileId);
}

/// <summary>
/// Un CV que quedó afuera de la ejecución, y por qué.
/// </summary>
public sealed record FailedCv(string FileName, string Reason);

/// <summary>
/// Cómo terminó la subida de un lote.
/// </summary>
public sealed record UploadOutcome(IReadOnlyList<UploadedCv> Ready, IReadOnlyList<FailedCv> Failed);

/// <summary>
/// El desenlace de cada archivo del lote, resuelto contra el dataset.
/// </summary>
public sealed record BatchOutcome(IReadOnlyList<CvFile> Processed, IReadOnlyList<FailedCv> ToReview);

/// <summary>
/// Cómo terminó un lote: qué desenlace tuvo cada archivo y qué no se pudo mover.
/// </summary>
public sealed record BatchResult(BatchOutcome Outcome, ArchiveResult Archive);
