using System.Buffers.Text;
using System.Text.Json;
using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Keeps the session's access token valid.
/// </summary>
public sealed class TokenStore(IAuthClient authClient, TimeProvider timeProvider)
{
    private static readonly TimeSpan s_refreshMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan s_assumedLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim gate = new(1, 1);

    private Session session = Session.None;
    private DateTimeOffset expiresAt;

    /// <summary>
    /// Opens the session with the user's credentials. The password is not retained.
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
    /// Returns a valid access token, refreshing it if it is about to expire.
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
    /// Forces a refresh even if the current token is not yet about to expire.
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
    /// Closes the session. The tokens only live in memory, so forgetting them closes it completely.
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

        // The refresh token does not rotate: the one the login returned is good for the whole session.
        Adopt(session with { AccessToken = refreshed });

        return refreshed;
    }

    private void Adopt(Session opened)
    {
        session = opened;
        expiresAt = ReadExpiry(opened.AccessToken) ?? timeProvider.GetUtcNow() + s_assumedLifetime;
    }

    /// <summary>
    /// Expiry declared by the token itself, or null if it cannot be read.
    /// </summary>
    private static DateTimeOffset? ReadExpiry(string accessToken)
    {
        string[] parts = accessToken.Split('.');

        if (parts.Length != 3)
            return null;

        try
        {
            // Signature not validated: only the expiry is read from this token, and validating it is the Hub's job.
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
