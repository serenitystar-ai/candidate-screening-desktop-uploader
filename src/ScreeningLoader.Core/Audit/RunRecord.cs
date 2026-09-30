using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Audit;

/// <summary>
/// A previous run, rebuilt from its audit log.
/// </summary>
/// <param name="OpeningTitle">
/// Empty for runs logged before the title was saved; the host resolves it in that case.
/// </param>
public sealed record RunRecord(
    string Id,
    DateTimeOffset StartedAt,
    string OpeningId,
    string OpeningTitle,
    RunTotals? Totals,
    IReadOnlyList<FailedCv> ToReview);
