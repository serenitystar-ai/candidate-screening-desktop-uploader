using System.Text.Json;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Un avance de la corrida, para que el host lo muestre.
/// </summary>
public abstract record RunEvent;

/// <summary>Empezó la subida de un archivo.</summary>
public sealed record FileUploading(string FileName, long SizeBytes) : RunEvent;

/// <summary>El Hub aceptó los bytes y todavía está procesando el archivo.</summary>
public sealed record FileProcessing(string FileName) : RunEvent;

/// <summary>El archivo quedó listo para viajar a una ejecución.</summary>
public sealed record FileUploaded(string FileName) : RunEvent;

/// <summary>El archivo no va a llegar a una ejecución.</summary>
public sealed record FileFailed(string FileName, string Reason) : RunEvent;

/// <summary>El dataset confirmó la fila del archivo y el archivo ya está archivado.</summary>
public sealed record FileRegistered(string FileName) : RunEvent;

/// <summary>Arrancó el turno de análisis del lote.</summary>
public sealed record BatchAnalyzing(int FileCount) : RunEvent;

/// <summary>El turno murió antes de acusar, y lo que haya escrito se reconcilia igual.</summary>
public sealed record BatchAnalyzeFailed(string Reason) : RunEvent;

/// <summary>El agente informa en qué paso va.</summary>
public sealed record AgentProgress(string Title, string? Description, int? EstimatedProgress) : RunEvent
{
    private const string ProgressTaskKey = "skills_ProgressNotifier_execute";

    /// <summary>
    /// Traduce un evento del stream a progreso, o null si el evento no lo lleva.
    /// </summary>
    internal static AgentProgress? Parse(string eventName, JsonElement data)
    {
        // El progreso llega al empezar cada paso: esperar el cierre reportaría cada hito tarde.
        if (eventName is not "task_start"
            || Text(data, "task_key") != ProgressTaskKey
            || !data.TryGetProperty("input", out JsonElement input)
            || input.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        return new AgentProgress(
            Text(input, "progressTitle"),
            Text(input, "progressDescription") is { Length: > 0 } description ? description : null,
            Number(input, "estimatedProgress"));
    }

    private static string Text(JsonElement input, string property) =>
        input.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()!
            : string.Empty;

    /// <summary>
    /// Un entero de los argumentos de la herramienta, que el agente manda como número o como texto.
    /// </summary>
    private static int? Number(JsonElement input, string property)
    {
        if (!input.TryGetProperty(property, out JsonElement value))
            return null;

        // TryGetInt32 lanza si el elemento no es un número, así que el tipo se chequea antes.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null
        };
    }
}

/// <summary>Arrancó la corrida.</summary>
public sealed record RunStarted(int BatchCount, int FileCount) : RunEvent;

/// <summary>Arrancó un lote.</summary>
public sealed record BatchStarted(int Number, int Of, int FileCount) : RunEvent;

/// <summary>Se está esperando para reintentar una condición que se resuelve sola.</summary>
public sealed record RetryWaiting(string Operation, int Attempt, TimeSpan Delay, string Reason) : RunEvent;

/// <summary>Terminó la corrida.</summary>
public sealed record RunCompleted(RunTotals Totals) : RunEvent;

/// <summary>
/// Cómo terminó cada archivo de la corrida.
/// </summary>
public sealed record RunTotals(
    int Processed,
    int ToReview,
    int Rejected,
    int FailedMoves);

/// <summary>Terminó un lote. Lo consume el orquestador para acumular totales.</summary>
public sealed record BatchFinished(int Number, BatchResult Result) : RunEvent;
