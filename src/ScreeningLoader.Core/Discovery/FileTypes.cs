namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Mapping between the MIME types the agent accepts and the file extensions on disk.
/// </summary>
internal static class FileTypes
{
    /// <summary>
    /// The two directions are written separately because they are not symmetric: one MIME covers several
    /// extensions and several MIMEs cover the same one.
    /// </summary>
    private static readonly Dictionary<string, string[]> s_extensionsByMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/msword"] = [".doc"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = [".docx"],
        ["application/excel"] = [".xls"],
        ["application/vnd.ms-excel"] = [".xls"],
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = [".xlsx"],
        ["application/pdf"] = [".pdf"],
        ["text/plain"] = [".txt"],
        ["text/csv"] = [".csv"],
        ["text/markdown"] = [".md"],
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"]
    };

    private static readonly Dictionary<string, string> s_mimeTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".md"] = "text/markdown",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    /// <summary>
    /// Translates the MIME types the agent publishes into the extensions searched for on disk.
    /// </summary>
    public static IReadOnlySet<string> ExtensionsFor(IReadOnlyList<string> mimeTypes)
    {
        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase);

        foreach (string mimeType in mimeTypes)
        {
            // The list mixes MIME types with the odd bare extension. An unknown MIME is not
            // accepted: better to reject one file too many than to upload one that will bounce.
            if (mimeType.StartsWith('.'))
                extensions.Add(mimeType);
            else if (s_extensionsByMimeType.TryGetValue(mimeType, out string[]? mapped))
                extensions.UnionWith(mapped);
        }

        return extensions;
    }

    /// <summary>
    /// The Content-Type a file is sent with in the upload.
    /// </summary>
    public static string ContentTypeFor(string fileName) =>
        s_mimeTypeByExtension.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}
