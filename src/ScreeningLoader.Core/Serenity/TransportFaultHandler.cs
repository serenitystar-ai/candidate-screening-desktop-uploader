using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Flags the engine sets on a request to change how the transport treats it.
/// </summary>
internal static class RequestOptions
{
    /// <summary>A request whose response is consumed by streaming and does not allow a per-request cap.</summary>
    public static readonly HttpRequestOptionsKey<bool> LongRunning = new("screeningLoader.longRunning");
}

/// <summary>
/// Applies the per-request cap and translates transport failures to the engine's taxonomy.
/// </summary>
internal sealed class TransportFaultHandler(TimeSpan requestTimeout)
    : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        // The HttpClient has no timeout of its own: its timeout also covers reading the body, and would cut off a
        // streaming turn midway. The cap is set here, and a long execution is exempt.
        CancellationTokenSource? attempt = null;

        try
        {
            CancellationToken token = ct;

            if (!request.Options.TryGetValue(RequestOptions.LongRunning, out bool longRunning) || !longRunning)
            {
                attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(requestTimeout);
                token = attempt.Token;
            }

            return await base.SendAsync(request, token);
        }
        catch (HttpRequestException ex)
        {
            throw new ScreeningLoaderException(ErrorKind.Transient, "No se pudo contactar al AI Hub.", ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ScreeningLoaderException(
                ErrorKind.Transient,
                "El AI Hub no respondió dentro del tiempo de espera.",
                ex);
        }
        finally
        {
            attempt?.Dispose();
        }
    }
}
