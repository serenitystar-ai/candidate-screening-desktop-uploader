namespace ScreeningLoader.Core.Run;

/// <summary>
/// A file that could not be moved, and why.
/// </summary>
public sealed record FailedMove(string FileName, string Reason);

/// <summary>
/// What was left unmoved when sorting a batch.
/// </summary>
public sealed record ArchiveResult(IReadOnlyList<FailedMove> FailedMoves);
