using ScreeningLoader.Core.Discovery;
using ScreeningLoader.Core.Screening;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Resolución de qué archivo produjo cada fila.
/// </summary>
internal static class Reconciler
{
    /// <summary>
    /// Asigna a cada archivo del lote su desenlace.
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
                // Un CV que desaparece sin fila y sin explicación es el único desenlace que nadie detecta
                // contando, así que se lo trata como fallido en vez de dejarlo en pendientes.
                toReview.Add(new FailedCv(file.FileName, "El agente no lo acusó ni insertado ni fallido."));
        }

        return new BatchOutcome(processed, toReview);
    }
}
