using Doggo.Scraper.News;

namespace Doggo.Scraper;

/// <summary>
/// News scraping pipeline: a periodic scan of the registered sources, with per-source
/// scheduling and circuit breaking. See docs/guide/features.md.
/// </summary>
public sealed class ScraperWorker(
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<ScraperOptions> options,
    ILogger<ScraperWorker> logger) : BackgroundService
{
    private readonly int _scanIntervalSeconds = Math.Max(options.Value.ScanIntervalSeconds, 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scraper worker started (scan interval {Seconds}s)", _scanIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The scheduler and its adapters are scoped (typed HttpClients); one scope per scan.
                using var scope = scopeFactory.CreateScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<ScraperScheduler>();
                await scheduler.RunDueSourcesAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scraper scan failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_scanIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
