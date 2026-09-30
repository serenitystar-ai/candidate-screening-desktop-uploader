using System.Net;

namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Engine error that declares how it must be handled.
/// </summary>
public class ScreeningLoaderException(ErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorKind Kind { get; } = kind;

    /// <summary>Status the AI Hub responded with, when the error came from a call.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>How long the server asked to wait before retrying, if it said so.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
