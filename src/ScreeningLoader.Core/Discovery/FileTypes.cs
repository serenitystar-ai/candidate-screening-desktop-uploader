namespace ScreeningLoader.Core.Discovery;

/// <summary>
/// Correspondencia entre los MIME types que acepta el agente y las extensiones en disco.
/// </summary>
internal static class FileTypes
{
    /// <summary>
    /// Las dos direcciones se escriben aparte porque no son simétricas: un MIME cubre varias extensiones
    /// y varios MIME cubren la misma.
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
    /// Traduce los MIME types que publica el agente a las extensiones que se buscan en disco.
    /// </summary>
    public static IReadOnlySet<string> ExtensionsFor(IReadOnlyList<string> mimeTypes)
    {
        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase);

        foreach (string mimeType in mimeTypes)
        {
            // La lista mezcla MIME types con alguna extensión suelta. Un MIME que no conocemos no se
            // acepta: es preferible descartar un archivo de más que subir uno que va a rebotar.
            if (mimeType.StartsWith('.'))
                extensions.Add(mimeType);
            else if (s_extensionsByMimeType.TryGetValue(mimeType, out string[]? mapped))
                extensions.UnionWith(mapped);
        }

        return extensions;
    }

    /// <summary>
    /// El Content-Type con el que viaja un archivo en la subida.
    /// </summary>
    public static string ContentTypeFor(string fileName) =>
        s_mimeTypeByExtension.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}
