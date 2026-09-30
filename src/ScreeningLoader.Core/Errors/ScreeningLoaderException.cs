using System.Net;

namespace ScreeningLoader.Core.Errors;

/// <summary>
/// Error del motor que declara cómo debe tratarse.
/// </summary>
public class ScreeningLoaderException(ErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ErrorKind Kind { get; } = kind;

    /// <summary>Estado con el que respondió el AI Hub, cuando el error vino de una llamada.</summary>
    public HttpStatusCode? StatusCode { get; init; }

    /// <summary>Cuánto pidió el servidor que se espere antes de reintentar, si lo dijo.</summary>
    public TimeSpan? RetryAfter { get; init; }
}
