namespace Doggo.Scraper;

/// <summary>
/// News scraping pipeline placeholder. Source adapters, scheduling and
/// deduplication land in milestone M2 (see docs/guide/features.md).
/// </summary>
public class ScraperWorker(ILogger<ScraperWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scraper worker started (pipeline not implemented yet)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
