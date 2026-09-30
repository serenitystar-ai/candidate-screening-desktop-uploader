using System.Buffers.Text;
using System.Text.Json;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Mantiene vigente el access token de la sesión.
/// </summary>
public sealed class TokenStore(IAuthClient authClient, TimeProvider timeProvider)
{
    private static readonly TimeSpan s_refreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_assumedLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim gate = new(1, 1);

    private Session session = Session.None;
    private DateTimeOffset expiresAt;

    /// <summary>
    /// Abre la sesión con las credenciales del usuario. La contraseña no se retiene.
    /// </summary>
    public async Task LoginAsync(string email, string password, CancellationToken ct)
    {
        Session opened = await authClient.LoginAsync(email, password, ct);

        await gate.WaitAsync(ct);
        try
        {
            Adopt(opened);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Devuelve un access token vigente, refrescándolo si está por vencer.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireSession();

            return timeProvider.GetUtcNow() < expiresAt - s_refreshMargin
                ? session.AccessToken
                : await RefreshAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Fuerza un refresco aunque el token vigente todavía no esté por vencer.
    /// </summary>
    public async Task<string> ForceRefreshAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            RequireSession();

            return await RefreshAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Cierra la sesión. Los tokens sólo viven en memoria, así que olvidarlos es cerrarla del todo.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            session = Session.None;
            expiresAt = default;
        }
        finally
        {
            gate.Release();
        }
    }

    private void RequireSession()
    {
        if (!session.IsAuthenticated)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "No hay una sesión iniciada.");
    }

    private async Task<string> RefreshAsync(CancellationToken ct)
    {
        string refreshed = await authClient.RefreshAsync(session, ct);

        // El refresh token no rota: el que devolvió el login sirve para toda la sesión.
        Adopt(session with { AccessToken = refreshed });

        return refreshed;
    }

    private void Adopt(Session opened)
    {
        session = opened;
        expiresAt = ReadExpiry(opened.AccessToken) ?? timeProvider.GetUtcNow() + s_assumedLifetime;
    }

    /// <summary>
    /// Vencimiento que declara el propio token, o null si no se puede leer.
    /// </summary>
    private static DateTimeOffset? ReadExpiry(string accessToken)
    {
        string[] parts = accessToken.Split('.');

        if (parts.Length != 3)
            return null;

        try
        {
            // Sin validar la firma: de este token sólo se lee cuándo vence, y validarlo es tarea del Hub.
            using JsonDocument payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));

            return payload.RootElement.TryGetProperty("exp", out JsonElement exp) && exp.TryGetInt64(out long seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }
}
