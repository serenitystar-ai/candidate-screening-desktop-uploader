namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// A file in the folder that the engine is going to process.
/// </summary>
public sealed record CvFile(string Path, string FileName, long SizeBytes);
