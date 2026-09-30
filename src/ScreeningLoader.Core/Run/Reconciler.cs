using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Resolves which file produced each row.
/// </summary>
internal static class Reconciler
{
    /// <summary>
    /// Assigns each file in the batch its outcome.
    /// </summary>
    public static BatchOutcome Reconcile(
        Batch batch,
        IReadOnlyList<InsertedCandidate> inserted,
        IReadOnlyList<FailedCv> failed)
    {
        HashSet<string> insertedNames = [.. inserted.Select(c => c.CvFileName)];
        Dictionary<string, FailedCv> failures = failed
            .GroupBy(f => f.FileName)
            .ToDictionary(group => group.Key, group => group.First());

        List<CvFile> processed = [];
        List<FailedCv> toReview = [];

        foreach (CvFile file in batch.Files)
        {
            if (insertedNames.Contains(file.FileName))
                processed.Add(file);
            else if (failures.TryGetValue(file.FileName, out FailedCv? failure))
                toReview.Add(failure);
            else
                // A CV that vanishes with no row and no explanation is the only outcome nobody catches
                // by counting, so it is treated as failed instead of being left pending.
                toReview.Add(new FailedCv(file.FileName, "El agente no lo acusó ni insertado ni fallido."));
        }

        return new BatchOutcome(processed, toReview);
    }
}
