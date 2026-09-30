namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Un lote de archivos y su peso acumulado.
/// </summary>
public sealed record Batch(IReadOnlyList<CvFile> Files)
{
    public long TotalBytes { get; } = Files.Sum(file => file.SizeBytes);

    public double TotalMegabytes => TotalBytes / (double)(1024 * 1024);
}
