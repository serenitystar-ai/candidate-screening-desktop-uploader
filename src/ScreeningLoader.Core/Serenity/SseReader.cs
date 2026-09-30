using System.Text.Json;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Reading of an execution's event stream.
/// </summary>
internal static class SseReader
{
    private const string EventPrefix = "event:";
    private const string DataPrefix = "data:";
    private const string Done = "[DONE]";

    public const string Error = "error";
    public const string Stop = "stop";
    public const string TaskStart = "task_start";

    /// <summary>
    /// Consumes the stream until it closes and returns the turn's result.
    /// </summary>
    public static async Task<JsonElement> ReadUntilStopAsync(
        Stream stream,
        Action<string, JsonElement> onEvent,
        CancellationToken ct)
    {
        JsonElement? result = null;

        await foreach ((string name, JsonElement data) in ReadEventsAsync(stream, ct))
        {
            onEvent(name, data);

            if (name is Error)
                throw new ScreeningLoaderException(ErrorKind.Transient, ReadErrorMessage(data));

            if (name is Stop && data.TryGetProperty("result", out JsonElement turn))
                result = turn;
        }

        // A stream that ends without closing is a failure, not an empty turn.
        return result ?? throw new ScreeningLoaderException(
            ErrorKind.Transient,
            "The execution stream ended without a closing event.");
    }

    private static async IAsyncEnumerable<(string Name, JsonElement Data)> ReadEventsAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using StreamReader reader = new(stream);

        string? announced = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith(EventPrefix, StringComparison.Ordinal))
            {
                // The Hub writes a space before the line break: the name arrives as "stop ".
                announced = line[EventPrefix.Length..].Trim();
                continue;
            }

            if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
                continue;

            string payload = line[DataPrefix.Length..].Trim();

            if (payload.Length == 0 || payload == Done)
                continue;

            if (!TryParse(payload, out JsonElement data))
                continue;

            // The name travels in the event: line and again inside the JSON; the one in the body is preferred.
            string name = data.TryGetProperty("type", out JsonElement type)
                && type.ValueKind is JsonValueKind.String
                    ? type.GetString()!
                    : announced ?? string.Empty;

            announced = null;

            yield return (name, data);
        }
    }

    private static bool TryParse(string payload, out JsonElement data)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            data = document.RootElement.Clone();

            return true;
        }
        catch (JsonException)
        {
            data = default;

            return false;
        }
    }

    private static string ReadErrorMessage(JsonElement data) =>
        data.TryGetProperty("message", out JsonElement message) && message.ValueKind is JsonValueKind.String
            ? message.GetString()!
            : "The execution failed without detail.";
}
