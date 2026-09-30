using ScreeningLoader.Core.Errors;

namespace ScreeningLoader.Core.Run;

/// <summary>
/// Retry with backoff for conditions that resolve by waiting.
/// </summary>
public sealed class RetryPolicy(ScreeningLoaderOptions options, ErrorLog errorLog, TimeProvider timeProvider)
{
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs an operation, retrying the conditions that resolve by waiting.
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
            // The policy is read from the taxonomy: only what the error declares transient is retried.
            catch (ScreeningLoaderException ex)
                when (ex.Kind is ErrorKind.Transient && attempt < options.RetryMaxAttempts)
            {
                TimeSpan delay = DelayFor(attempt, ex);

                errorLog.Write($"{operation} attempt={attempt}", ex);
                emit(new RetryWaiting(operation, attempt, delay, ex.Message));

                await Task.Delay(delay, timeProvider, ct);
            }
        }
    }

    /// <summary>
    /// Exponential backoff, honoring the server's Retry-After when it sends one.
    /// </summary>
    private TimeSpan DelayFor(int attempt, ScreeningLoaderException ex)
    {
        if (ex.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            return retryAfter < s_maxDelay ? retryAfter : s_maxDelay;

        // The real limit may come from the plan or the agent, not from the general rules, so it backs off
        // quickly instead of insisting with a fixed wait.
        TimeSpan backoff = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs * Math.Pow(2, attempt - 1));

        return backoff < s_maxDelay ? backoff : s_maxDelay;
    }
}
