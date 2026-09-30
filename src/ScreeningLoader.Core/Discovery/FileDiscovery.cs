using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Discovery and filtering of the files in a folder.
/// </summary>
public sealed class FileDiscovery(ScreeningLoaderOptions options)
{
    /// <summary>
    /// Classifies the files in a folder as accepted or rejected, and builds the batches.
    /// </summary>
    public DiscoveryResult Discover(string folder, IReadOnlyList<string> acceptedMimeTypes)
    {
        IReadOnlySet<string> acceptedExtensions = FileTypes.ExtensionsFor(acceptedMimeTypes);

        List<CvFile> accepted = [];
        List<RejectedFile> rejected = [];

        foreach (FileInfo file in EnumerateFiles(folder))
        {
            RejectionReason? reason = Classify(file, acceptedExtensions);

            if (reason is { } rejection)
                rejected.Add(new RejectedFile(file.Name, rejection));
            else
                accepted.Add(new CvFile(file.FullName, file.Name, file.Length));
        }

        return new DiscoveryResult(accepted, rejected, IntoBatches(accepted), Failed(folder));
    }

    /// <summary>
    /// Moves the files that were in the failed folder back to the folder.
    /// </summary>
    /// <returns>How many were moved.</returns>
    public int Restore(string folder, IEnumerable<string> fileNames)
    {
        HashSet<string> wanted = [.. fileNames];
        int moved = 0;

        foreach (CvFile file in Failed(folder).Where(file => wanted.Contains(file.FileName)))
        {
            // A file that cannot be moved is not counted and the rest carry on: the next read of the
            // folder shows what was left, so there is no need to report it any other way.
            try
            {
                File.Move(file.Path, Unique(folder, file.FileName), overwrite: false);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return moved;
    }

    /// <summary>
    /// What is in the failed subfolder, unfiltered: it is restored so it can be retried, and the
    /// filter runs again once it is back in the folder.
    /// </summary>
    private IReadOnlyList<CvFile> Failed(string folder)
    {
        DirectoryInfo failed = new(Path.Combine(folder, options.ReviewFolderName));

        if (!failed.Exists)
            return [];

        return
        [
            .. failed.EnumerateFiles()
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new CvFile(file.FullName, file.Name, file.Length))
        ];
    }

    /// <summary>
    /// A free destination, so that restoring a file does not overwrite another with the same name.
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

    /// <summary>
    /// Groups the accepted files into batches, splitting by count or by combined size.
    /// </summary>
    public IReadOnlyList<Batch> IntoBatches(IReadOnlyList<CvFile> files)
    {
        List<Batch> batches = [];
        List<CvFile> current = [];
        long currentBytes = 0;

        foreach (CvFile file in files)
        {
            bool full = current.Count == options.BatchSize
                || currentBytes + file.SizeBytes > options.MaxBatchBytes;

            if (full && current.Count > 0)
            {
                batches.Add(new Batch(current));
                current = [];
                currentBytes = 0;
            }

            current.Add(file);
            currentBytes += file.SizeBytes;
        }

        if (current.Count > 0)
            batches.Add(new Batch(current));

        return batches;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string folder)
    {
        DirectoryInfo directory = new(folder);

        if (!directory.Exists)
            throw new ScreeningLoaderException(ErrorKind.Fatal, $"La carpeta '{folder}' no existe.");

        // No recursion: procesados/ and fallidos/ are subfolders of this one and must not come back in.
        return directory.EnumerateFiles().OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase);
    }

    private RejectionReason? Classify(FileInfo file, IReadOnlySet<string> acceptedExtensions)
    {
        if (!acceptedExtensions.Contains(file.Extension))
            return RejectionReason.UnsupportedType;

        if (file.Length == 0)
            return RejectionReason.Empty;

        if (file.Length > options.MaxFileSizeMb * 1024L * 1024L)
            return RejectionReason.TooLarge;

        // A file that does not fit in a batch on its own is rejected here, rather than sent off to bounce.
        if (file.Length > options.MaxBatchBytes)
            return RejectionReason.ExceedsBatchBudget;

        return null;
    }
}
