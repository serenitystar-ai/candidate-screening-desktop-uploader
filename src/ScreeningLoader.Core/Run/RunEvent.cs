using System.Text.Json;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// A step forward in the run, for the host to display.
/// </summary>
public abstract record RunEvent;

/// <summary>A file's upload started.</summary>
public sealed record FileUploading(string FileName, long SizeBytes) : RunEvent;

/// <summary>The Hub accepted the bytes and is still processing the file.</summary>
public sealed record FileProcessing(string FileName) : RunEvent;

/// <summary>The file is ready to be sent to an execution.</summary>
public sealed record FileUploaded(string FileName) : RunEvent;

/// <summary>The file will not reach an execution.</summary>
public sealed record FileFailed(string FileName, string Reason) : RunEvent;

/// <summary>The dataset confirmed the file's row and the file is already archived.</summary>
public sealed record FileRegistered(string FileName) : RunEvent;

/// <summary>The batch's analysis turn started.</summary>
public sealed record BatchAnalyzing(int FileCount) : RunEvent;

/// <summary>The turn died before acknowledging, and whatever it wrote is reconciled anyway.</summary>
public sealed record BatchAnalyzeFailed(string Reason) : RunEvent;

/// <summary>The agent reports which step it is on.</summary>
public sealed record AgentProgress(string Title, string? Description, int? EstimatedProgress) : RunEvent
{
    private const string ProgressTaskKey = "skills_ProgressNotifier_execute";

    /// <summary>
    /// Translates a stream event into progress, or null if the event carries none.
    /// </summary>
    internal static AgentProgress? Parse(string eventName, JsonElement data)
    {
        // Progress arrives when each step starts: waiting for it to end would report every milestone late.
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
    /// An integer from the tool's arguments, which the agent sends as a number or as text.
    /// </summary>
    private static int? Number(JsonElement input, string property)
    {
        if (!input.TryGetProperty(property, out JsonElement value))
            return null;

        // TryGetInt32 throws if the element is not a number, so the type is checked first.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out int parsed) => parsed,
            _ => null
        };
    }
}

/// <summary>The run started.</summary>
public sealed record RunStarted(int BatchCount, int FileCount) : RunEvent;

/// <summary>A batch started.</summary>
public sealed record BatchStarted(int Number, int Of, int FileCount) : RunEvent;

/// <summary>Waiting to retry a condition that resolves on its own.</summary>
public sealed record RetryWaiting(string Operation, int Attempt, TimeSpan Delay, string Reason) : RunEvent;

/// <summary>The run finished.</summary>
public sealed record RunCompleted(RunTotals Totals) : RunEvent;

/// <summary>
/// How each file in the run ended up.
/// </summary>
public sealed record RunTotals(
    int Processed,
    int ToReview,
    int Rejected,
    int FailedMoves);

/// <summary>A batch finished. Consumed by the orchestrator to accumulate totals.</summary>
public sealed record BatchFinished(int Number, BatchResult Result) : RunEvent;
