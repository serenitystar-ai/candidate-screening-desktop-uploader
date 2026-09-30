namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// The result of a volatile knowledge upload.
/// </summary>
public sealed record VolatileKnowledgeRecord
{
    /// <summary>The identifier that anchors the file to the execution.</summary>
    public required Guid Id { get; init; }

    /// <summary>The identifier the file is downloaded with; it may come empty.</summary>
    public Guid? FileId { get; init; }

    public required string Status { get; init; }

    /// <summary>The file finished processing and the agent can read it.</summary>
    public bool IsReady => Status.Equals(Ready, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Hub is still processing it.</summary>
    public bool IsPending => Status.Equals(Pending, StringComparison.OrdinalIgnoreCase);

    private const string Ready = "success";
    private const string Pending = "analyzing";
}
