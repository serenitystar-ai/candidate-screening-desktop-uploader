namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// A batch of files and their combined size.
/// </summary>
public sealed record Batch(IReadOnlyList<CvFile> Files)
{
    public long TotalBytes { get; } = Files.Sum(file => file.SizeBytes);

    public double TotalMegabytes => TotalBytes / (double)(1024 * 1024);
}
