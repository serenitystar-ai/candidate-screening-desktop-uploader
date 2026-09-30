namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Access to the AI Hub account.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Opens a session with the user's credentials.
    /// </summary>
    Task<Session> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>
    /// Returns a new access token for the session.
    /// </summary>
    Task<string> RefreshAsync(Session session, CancellationToken ct);
}
