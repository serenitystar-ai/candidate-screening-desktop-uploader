using ScreeningLoader.Core.Discovery;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Reparto de los archivos de un lote entre procesados y fallidos.
/// </summary>
public sealed class FileArchiver(ScreeningLoaderOptions options)
{
    /// <summary>
    /// Mueve cada archivo del lote a la carpeta que le corresponde según su desenlace.
    /// </summary>
    public ArchiveResult Archive(BatchOutcome outcome, string sourceFolder)
    {
        List<FailedMove> failedMoves = [];

        string processed = Path.Combine(sourceFolder, options.ProcessedFolderName);
        string review = Path.Combine(sourceFolder, options.ReviewFolderName);

        foreach (CvFile file in outcome.Processed)
            TryMove(file.Path, file.FileName, processed, failedMoves);

        // Todo lo fallido sale de pendientes sin clasificar la causa: un PDF ilegible no se vuelve legible
        // solo, y dejarlo donde está lo hace fallar en cada corrida futura. La persona decide qué devolver.
        foreach (FailedCv failure in outcome.ToReview)
            TryMove(Path.Combine(sourceFolder, failure.FileName), failure.FileName, review, failedMoves);

        return new ArchiveResult(failedMoves);
    }

    private static void TryMove(
        string sourcePath,
        string fileName,
        string destinationFolder,
        List<FailedMove> failedMoves)
    {
        // Un move que falla no aborta la corrida: cuesta un duplicado posible en la próxima, y abortar
        // cuesta el resto del trabajo.
        try
        {
            Directory.CreateDirectory(destinationFolder);
            File.Move(sourcePath, Unique(destinationFolder, fileName), overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failedMoves.Add(new FailedMove(fileName, ex.Message));
        }
    }

    /// <summary>
    /// Un destino libre, para que archivar no pise un archivo que ya está ahí.
    /// </summary>
    private static string Unique(string folder, string fileName)
    {
        string candidate = Path.Combine(folder, fileName);

        if (!File.Exists(candidate))
            return candidate;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int suffix = 2; ; suffix++)
        {
            candidate = Path.Combine(folder, $"{stem} ({suffix}){extension}");

            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
