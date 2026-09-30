using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Reintento con backoff ante las condiciones que se resuelven esperando.
/// </summary>
public sealed class RetryPolicy(ScreeningLoaderOptions options, ErrorLog errorLog, TimeProvider timeProvider)
{
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Ejecuta una operación reintentando las condiciones que se resuelven esperando.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> action,
        Action<RunEvent> emit,
        CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action(ct);
            }
            // La política se lee de la taxonomía: sólo se reintenta lo que el error declara transitorio.
            catch (ScreeningLoaderException ex)
                when (ex.Kind is ErrorKind.Transient && attempt < options.RetryMaxAttempts)
            {
                TimeSpan delay = DelayFor(attempt, ex);

                errorLog.Write($"{operation} intento={attempt}", ex);
                emit(new RetryWaiting(operation, attempt, delay, ex.Message));

                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    /// <summary>
    /// Backoff exponencial, respetando el Retry-After del servidor cuando lo manda.
    /// </summary>
    private TimeSpan DelayFor(int attempt, ScreeningLoaderException ex)
    {
        if (ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            return retryAfter < s_maxDelay ? retryAfter : s_maxDelay;

        // El límite real puede venir del plan o del agente, no de las reglas generales, así que se cede
        // terreno rápido en vez de insistir con una espera fija.
        TimeSpan backoff = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

        return backoff < s_maxDelay ? backoff : s_maxDelay;
    }
}
