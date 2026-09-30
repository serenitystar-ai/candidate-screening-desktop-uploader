using System.Text.Json;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Acuse de una ejecución: los identificadores insertados y los archivos que fallaron.
/// </summary>
/// <param name="UnreadableIds">
/// Cuántos identificadores del recibo no eran un GUID. El agente los escribe, y a veces los escribe mal.
/// </param>
public sealed record AnalyzeReceipt(
    IReadOnlyList<string> InsertedIds,
    IReadOnlyList<FailedCv> Failed,
    int UnreadableIds = 0)
{
    public static AnalyzeReceipt Empty { get; } = new([], []);

    /// <summary>
    /// Lee el recibo del turno. Sólo el sobre de error del agente aborta.
    /// </summary>
    public static AnalyzeReceipt Parse(string content)
    {
        // Tolerante al revés que el resto del motor: para cuando esto corre, el agente ya escribió filas y
        // esas filas son candidatos reales. Un recibo ilegible no puede descartar una corrida cuyos
        // resultados están en la base; la reconciliación contra el dataset es la que manda.
        if (!TryParseRoot(content, out JsonElement root))
            return Empty;

        if (root.TryGetProperty("status", out JsonElement status)
            && status.ValueKind is JsonValueKind.String
            && status.GetString() is "error")
        {
            throw new ScreeningLoaderException(ErrorKind.Transient, ReadErrorMessage(root));
        }

        (IReadOnlyList<string> ids, int unreadable) = ReadInsertedIds(root);

        return new AnalyzeReceipt(ids, ReadFailed(root), unreadable);
    }

    private static bool TryParseRoot(string content, out JsonElement root)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(StripFences(content));
            root = document.RootElement.Clone();

            return root.ValueKind is JsonValueKind.Object;
        }
        catch (JsonException)
        {
            root = default;

            return false;
        }
    }

    /// <summary>
    /// Quita la cerca de código que el modelo a veces agrega pese a tenerlo prohibido.
    /// </summary>
    private static string StripFences(string content)
    {
        string trimmed = content.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        int firstBreak = trimmed.IndexOf('\n');
        int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

        return firstBreak >= 0 && lastFence > firstBreak
            ? trimmed[(firstBreak + 1)..lastFence].Trim()
            : trimmed;
    }

    private static string ReadErrorMessage(JsonElement root) =>
        root.TryGetProperty("error", out JsonElement error)
        && error.TryGetProperty("message", out JsonElement message)
        && message.ValueKind is JsonValueKind.String
            ? message.GetString()!
            : "El agente devolvió un sobre de error sin detalle.";

    /// <summary>
    /// Los identificadores que el agente dice haber escrito, quedándose sólo con los que son un GUID.
    /// </summary>
    private static (IReadOnlyList<string> Ids, int Unreadable) ReadInsertedIds(JsonElement root)
    {
        if (!root.TryGetProperty("inserted", out JsonElement inserted)
            || inserted.ValueKind is not JsonValueKind.Array)
        {
            return ([], 0);
        }

        List<string> ids = [];
        int unreadable = 0;

        foreach (JsonElement row in inserted.EnumerateArray())
        {
            if (row.ValueKind is not JsonValueKind.Object
                || !row.TryGetProperty("id", out JsonElement id)
                || id.ValueKind is not JsonValueKind.String
                || id.GetString() is not { Length: > 0 } value)
            {
                unreadable++;
                continue;
            }

            // Un id que no es un GUID no puede llegar a una sentencia: el helper lo rechazaría y se
            // llevaría puesta la corrida. Se cuenta como ilegible y la fila se confirma por nombre.
            if (Guid.TryParse(value, out _))
                ids.Add(value);
            else
                unreadable++;
        }

        return (ids, unreadable);
    }

    private static IReadOnlyList<FailedCv> ReadFailed(JsonElement root)
    {
        if (!root.TryGetProperty("failed", out JsonElement failed)
            || failed.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. failed.EnumerateArray()
                .Where(row => row.ValueKind is JsonValueKind.Object)
                .Select(row => new FailedCv(Text(row, "fileName"), Text(row, "reason")))
                .Where(cv => cv.FileName.Length > 0)
        ];
    }

    private static string Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
}
