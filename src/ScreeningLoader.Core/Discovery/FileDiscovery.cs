using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Descubrimiento y filtrado de los archivos de una carpeta.
/// </summary>
public sealed class FileDiscovery(ScreeningLoaderOptions options)
{
    /// <summary>
    /// Clasifica los archivos de una carpeta en aceptados y descartados, y arma los lotes.
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
    /// Devuelve a la carpeta los archivos que estaban en la de fallidos.
    /// </summary>
    /// <returns>Cuántos se movieron.</returns>
    public int Restore(string folder, IEnumerable<string> fileNames)
    {
        HashSet<string> wanted = [.. fileNames];
        int moved = 0;

        foreach (CvFile file in Failed(folder).Where(file => wanted.Contains(file.FileName)))
        {
            // Un archivo que no se puede mover deja de contar y los demás siguen: la próxima lectura de la
            // carpeta muestra lo que quedó, así que no hace falta avisar de otra forma.
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
    /// Lo que hay en la subcarpeta de fallidos, sin filtrar: se devuelve para poder reintentarlo, y el
    /// filtro corre de nuevo cuando vuelva a la carpeta.
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
    /// Un destino libre, para que devolver un archivo no pise otro con el mismo nombre.
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
    /// Agrupa los archivos aceptados en lotes, cortando por cantidad o por peso acumulado.
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

        // Sin recursión: procesados/ y fallidos/ son subcarpetas de ésta y no vuelven a entrar.
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

        // Un archivo que por sí solo no entra en un lote se descarta acá, en vez de mandarlo a rebotar.
        if (file.Length > options.MaxBatchBytes)
            return RejectionReason.ExceedsBatchBudget;

        return null;
    }
}
