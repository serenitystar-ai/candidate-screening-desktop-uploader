namespace ScreeningLoader.Core.Run;

/// <summary>
/// Un archivo que no se pudo mover, y por qué.
/// </summary>
public sealed record FailedMove(string FileName, string Reason);

/// <summary>
/// Qué quedó sin mover al repartir un lote.
/// </summary>
public sealed record ArchiveResult(IReadOnlyList<FailedMove> FailedMoves);
