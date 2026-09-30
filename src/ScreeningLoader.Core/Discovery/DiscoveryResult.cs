namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// What the engine found in a folder: what it processes, what it discards and in which batches.
/// </summary>
/// <param name="Failed">
/// What was left in the failed subfolder. It goes into no batch: it is there so it can be restored.
/// </param>
public sealed record DiscoveryResult(
    IReadOnlyList<CvFile> Accepted,
    IReadOnlyList<RejectedFile> Rejected,
    IReadOnlyList<Batch> Batches,
    IReadOnlyList<CvFile> Failed);
