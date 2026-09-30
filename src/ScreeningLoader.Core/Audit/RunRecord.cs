using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Audit;

/// <summary>
/// Una corrida anterior, reconstruida desde su registro de auditoría.
/// </summary>
/// <param name="OpeningTitle">
/// Vacío en las corridas registradas antes de que el título se guardara; el host lo resuelve entonces.
/// </param>
public sealed record RunRecord(
    string Id,
    DateTimeOffset StartedAt,
    string OpeningId,
    string OpeningTitle,
    RunTotals? Totals,
    IReadOnlyList<FailedCv> ToReview);
