namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Un archivo de la carpeta que el motor va a procesar.
/// </summary>
public sealed record CvFile(string Path, string FileName, long SizeBytes);
