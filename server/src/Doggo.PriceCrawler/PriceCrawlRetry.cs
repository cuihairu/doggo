namespace Doggo.PriceCrawler;

/// <summary>
/// Exponential backoff with jitter (design §5.2: base 5s, factor 2, cap 30min, ±25% jitter).
/// Only unexpected exceptions trigger retries; "site answered but no data" returns normally.
/// </summary>
public static class PriceCrawlRetry
{
    public static async Task<T> ExecuteAsync<T>(
        PriceCrawlerRetryOptions options,
        Func<int, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                if (attempt >= Math.Max(options.MaxAttempts, 1))
                {
                    throw;
                }

                await Task.Delay(Delay(options, attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static int Delay(PriceCrawlerRetryOptions options, int attempt)
    {
        var exponent = Math.Min(attempt - 1, 30);
        var seconds = Math.Min(options.BaseDelaySeconds * Math.Pow(options.Factor, exponent), options.CapSeconds);
        var jitter = seconds * options.JitterRatio * (Random.Shared.NextDouble() * 2 - 1);
        return Math.Max((int)(seconds + jitter), 1);
    }
}
