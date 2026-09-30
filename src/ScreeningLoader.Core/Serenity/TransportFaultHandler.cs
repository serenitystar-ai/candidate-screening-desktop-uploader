using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Serenity;

/// <summary>
/// Marcas que el motor pone en un pedido para cambiar cómo se lo trata en el transporte.
/// </summary>
internal static class RequestOptions
{
    /// <summary>Pedido cuya respuesta se consume por streaming y no admite un tope por pedido.</summary>
    public static readonly HttpRequestOptionsKey<bool> LongRunning = new("screeningLoader.longRunning");
}

/// <summary>
/// Aplica el tope por pedido y traduce los fallos de transporte a la taxonomía del motor.
/// </summary>
internal sealed class TransportFaultHandler(TimeSpan requestTimeout)
    : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        // El HttpClient va sin timeout propio: el suyo cubre también la lectura del cuerpo, y cortaría un
        // turno por streaming a mitad de camino. El tope se pone acá, y una ejecución larga queda exenta.
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
