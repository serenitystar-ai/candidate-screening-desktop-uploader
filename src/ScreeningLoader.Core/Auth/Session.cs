namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Tokens the user's session operates with.
/// </summary>
public sealed record Session(string AccessToken, string RefreshToken)
{
    /// <summary>Unauthenticated session.</summary>
    public static Session None { get; } = new(string.Empty, string.Empty);

    /// <summary>Whether the session has both tokens it needs to operate.</summary>
    public bool IsAuthenticated => AccessToken.Length > 0 && RefreshToken.Length > 0;
}
