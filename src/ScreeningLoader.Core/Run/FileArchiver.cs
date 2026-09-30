using ScreeningLoader.Core.Discovery;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Sorts a batch's files between procesados and fallidos.
/// </summary>
public sealed class FileArchiver(ScreeningLoaderOptions options)
{
    /// <summary>
    /// Moves each file in the batch to the folder that matches its outcome.
    /// </summary>
    public ArchiveResult Archive(BatchOutcome outcome, string sourceFolder)
    {
        List<FailedMove> failedMoves = [];

        string processed = Path.Combine(sourceFolder, options.ProcessedFolderName);
        string review = Path.Combine(sourceFolder, options.ReviewFolderName);

        foreach (CvFile file in outcome.Processed)
            TryMove(file.Path, file.FileName, processed, failedMoves);

        // All failures leave pending regardless of cause: an unreadable PDF does not become readable on its own,
        // and leaving it in place makes it fail on every future run. The user decides what to restore.
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
        // A failed move does not abort the run: it costs a possible duplicate on the next one, and aborting
        // costs the rest of the work.
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
    /// A free destination, so that archiving does not overwrite a file already there.
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
