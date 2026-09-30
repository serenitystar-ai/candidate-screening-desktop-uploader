using System.Text.Json;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Run;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Acknowledgment of an execution: the inserted identifiers and the files that failed.
/// </summary>
/// <param name="UnreadableIds">
/// How many identifiers in the receipt were not a GUID. The agent writes them, and sometimes writes them wrong.
/// </param>
public sealed record AnalyzeReceipt(
    IReadOnlyList<string> InsertedIds,
    IReadOnlyList<FailedCv> Failed,
    int UnreadableIds = 0)
{
    public static AnalyzeReceipt Empty { get; } = new([], []);

    /// <summary>
    /// Reads the turn's receipt. Only the agent's error envelope aborts.
    /// </summary>
    public static AnalyzeReceipt Parse(string content)
    {
        // Lenient, unlike the rest of the engine: by the time this runs, the agent has already written rows and
        // those rows are real candidates. An unreadable receipt cannot discard a run whose
        // results are in the database; the reconciliation against the dataset is what decides.
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
    /// Strips the code fence the model sometimes adds despite being told not to.
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
            : "The agent returned an error envelope without detail.";

    /// <summary>
    /// The identifiers the agent says it wrote, keeping only the ones that are a GUID.
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

            // An id that is not a GUID cannot reach a statement: the helper would reject it and take
            // the run down with it. It counts as unreadable and the row is confirmed by name.
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
