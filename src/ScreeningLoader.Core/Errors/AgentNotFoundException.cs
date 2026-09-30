namespace ScreeningLoader.Core.Errors;

/// <summary>
/// It could not be determined which agent serves the configured app.
/// </summary>
/// <remarks>
/// Kept apart from the other fatal errors because the host shows it on its own screen: it is the only one
/// fixed by changing a setting rather than by calling whoever administers the AI Hub.
/// </remarks>
public sealed class AgentNotFoundException(string message) : ScreeningLoaderException(ErrorKind.Fatal, message);
