using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Scraper.News;

/// <summary>Default page fetcher backed by <see cref="HttpClient"/>; retries transient failures.</summary>
public sealed class HttpPageFetcher(
    HttpClient http,
    ILogger<HttpPageFetcher> logger,
    IOptions<ScraperOptions> options) : IPageFetcher
{
    private const int MaxAttempts = 3;

    public async Task<string> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var baseDelay = TimeSpan.FromSeconds(Math.Max(options.Value.RetryDelaySeconds, 0));
        logger.LogDebug("Fetching {Url}", url);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await http.GetStringAsync(url, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromTicks(baseDelay.Ticks * attempt);
                logger.LogWarning(ex, "Fetch of {Url} failed (attempt {Attempt}/{Max}), retrying in {Seconds}s",
                    url, attempt, MaxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
