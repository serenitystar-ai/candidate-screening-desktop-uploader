namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Acceso a la cuenta del AI Hub.
/// </summary>
public interface IAuthClient
{
    /// <summary>
    /// Abre una sesión con las credenciales del usuario.
    /// </summary>
    Task<Session> LoginAsync(string email, string password, CancellationToken ct);

    /// <summary>
    /// Devuelve un access token nuevo para la sesión.
    /// </summary>
    Task<string> RefreshAsync(Session session, CancellationToken ct);
}
