using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// A CV that is ready to be sent to an execution.
/// </summary>
public sealed record UploadedCv(CvFile File, Guid VolatileKnowledgeId, Guid? FileId)
{
    internal static UploadedCv From(CvFile file, VolatileKnowledgeRecord record) =>
        new(file, record.Id, record.FileId);
}

/// <summary>
/// A CV that was left out of the execution, and why.
/// </summary>
public sealed record FailedCv(string FileName, string Reason);

/// <summary>
/// How a batch's upload ended.
/// </summary>
public sealed record UploadOutcome(IReadOnlyList<UploadedCv> Ready, IReadOnlyList<FailedCv> Failed);

/// <summary>
/// The outcome of each file in the batch, resolved against the dataset.
/// </summary>
public sealed record BatchOutcome(IReadOnlyList<CvFile> Processed, IReadOnlyList<FailedCv> ToReview);

/// <summary>
/// How a batch ended: the outcome of each file and what could not be moved.
/// </summary>
public sealed record BatchResult(BatchOutcome Outcome, ArchiveResult Archive);
