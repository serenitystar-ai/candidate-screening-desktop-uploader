using System.Text.Json;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Lectura del stream de eventos de una ejecución.
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
    /// Consume el stream hasta el cierre y devuelve el resultado del turno.
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

        // Un stream que termina sin cierre es un fallo, no un turno vacío.
        return result ?? throw new ScreeningLoaderException(
            ErrorKind.Transient,
            "El stream de la ejecución terminó sin evento de cierre.");
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
                // El Hub escribe un espacio antes del salto: el nombre llega como "stop ".
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

            // El nombre viaja en la línea event: y otra vez dentro del JSON; se prefiere el del cuerpo.
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
            : "La ejecución falló sin detalle.";
}
