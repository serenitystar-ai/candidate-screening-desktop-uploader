namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Lo que el motor encontró en una carpeta: qué procesa, qué descarta y en qué lotes.
/// </summary>
/// <param name="Failed">
/// Lo que quedó en la subcarpeta de fallidos. No entra a ningún lote: está para poder devolverlo.
/// </param>
public sealed record DiscoveryResult(
    IReadOnlyList<CvFile> Accepted,
    IReadOnlyList<RejectedFile> Rejected,
    IReadOnlyList<Batch> Batches,
    IReadOnlyList<CvFile> Failed);
