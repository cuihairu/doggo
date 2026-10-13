using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler;

/// <summary>
/// Hosted loop: one scheduler tick per ScanIntervalSeconds, each in its own DI scope
/// (same shape as the news scraper worker).
/// </summary>
public sealed class PriceCrawlerWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PriceCrawlerOptions> options,
    ILogger<PriceCrawlerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.ScanIntervalSeconds, 10));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await RunInScopeAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Price crawl tick failed");
            }
        }
    }

    private async Task RunInScopeAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<PriceCrawlerScheduler>();
        await scheduler.RunOnceAsync(cancellationToken).ConfigureAwait(false);
    }
}
