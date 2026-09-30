namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Tokens con los que opera la sesión del usuario.
/// </summary>
public sealed record Session(string AccessToken, string RefreshToken)
{
    /// <summary>Sesión sin autenticar.</summary>
    public static Session None { get; } = new(string.Empty, string.Empty);

    /// <summary>Indica si la sesión tiene los dos tokens que hacen falta para operar.</summary>
    public bool IsAuthenticated => AccessToken.Length > 0 && RefreshToken.Length > 0;
}
