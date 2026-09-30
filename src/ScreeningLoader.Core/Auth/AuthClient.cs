using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ScreeningLoader.Core.Errors;
using ScreeningLoader.Core.Serenity;

namespace ScreeningLoader.Core.Auth;

/// <summary>
/// Login and refresh against the AI Hub.
/// </summary>
public sealed class AuthClient(HttpClient httpClient) : IAuthClient
{
    private const string AccessTokenHeader = "X-Access-Token";
    private const string RefreshTokenHeader = "X-Refresh-Token";

    public async Task<Session> LoginAsync(string email, string password, CancellationToken ct)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            Routes.Login,
            new LoginReq(email, password, RememberMe: false),
            ct);

        // None of the login's failure outcomes is fixed by retrying.
        if (!response.IsSuccessStatusCode)
            throw await FatalAsync(response, ct);

        LoginRes? body = await response.Content.ReadFromJsonAsync<LoginRes>(ct);

        if (body?.Token is not { Length: > 0 } token || body.RefreshToken is not { Length: > 0 } refreshToken)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Fatal,
                "El AI Hub aceptó las credenciales pero no devolvió los tokens de la sesión.");
        }

        return new Session(token, refreshToken);
    }

    public async Task<string> RefreshAsync(Session session, CancellationToken ct)
    {
        if (!session.IsAuthenticated)
            throw new ScreeningLoaderException(ErrorKind.Fatal, "No hay una sesión iniciada para refrescar.");

        using HttpRequestMessage request = new(HttpMethod.Post, Routes.Refresh);

        // The endpoint binds both tokens from headers; a JSON body is ignored entirely.
        request.Headers.Add(AccessTokenHeader, session.AccessToken);
        request.Headers.Add(RefreshTokenHeader, session.RefreshToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, ct);

        // The refresh action does not catch its own errors, so the status code does not classify the failure.
        if (!response.IsSuccessStatusCode)
            throw await FatalAsync(response, ct);

        string token = await ReadTokenAsync(response, ct);

        return token.Length > 0
            ? token
            : throw new ScreeningLoaderException(ErrorKind.Fatal, "El AI Hub no devolvió un access token nuevo.");
    }

    /// <summary>
    /// Reads the returned JWT, whether it comes as a JSON string or as raw text.
    /// </summary>
    private static async Task<string> ReadTokenAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body = (await response.Content.ReadAsStringAsync(ct)).Trim();

        // Without [Produces] on the action, which of the two formatters wins depends on the negotiated Accept.
        if (!body.StartsWith('"'))
            return body;

        try
        {
            return JsonSerializer.Deserialize<string>(body) ?? string.Empty;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static async Task<ScreeningLoaderException> FatalAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        string message = await ReadLocalizedMessageAsync(response, ct) ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Email o contraseña incorrectas, o la cuenta está bloqueada.",
            HttpStatusCode.Forbidden =>
                "La cuenta no puede iniciar sesión: requiere doble factor, cambio de contraseña "
                + "o confirmación de un administrador.",
            _ => $"El AI Hub respondió {(int)response.StatusCode} al autenticar."
        };

        return new ScreeningLoaderException(ErrorKind.Fatal, message) { StatusCode = response.StatusCode };
    }

    /// <summary>
    /// Extracts the user-facing message from the LocalizedString that Account errors respond with.
    /// </summary>
    private static async Task<string?> ReadLocalizedMessageAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument document = await JsonDocument.ParseAsync(body, default, ct);

            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Object when document.RootElement.TryGetProperty("value", out JsonElement value)
                    && value.ValueKind is JsonValueKind.String => value.GetString(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LoginReq(string Email, string Password, bool RememberMe);

    private sealed record LoginRes(string? Token, string? RefreshToken);
}
