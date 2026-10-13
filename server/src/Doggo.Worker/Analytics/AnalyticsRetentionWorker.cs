using Doggo.Infrastructure.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Analytics;

/// <summary>
/// Daily event-detail purge at 02:00 UTC, after the 00:30 aggregation run, so every row
/// it removes is already rolled up into FunnelDailyStat (funnel design §10 #9).
/// </summary>
public sealed partial class AnalyticsRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AnalyticsOptions> options,
    ILogger<AnalyticsRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeOnly DueAt = new(2, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastCompleted = DateOnly.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var today = DateOnly.FromDateTime(now);
                if (TimeOnly.FromDateTime(now) >= DueAt && lastCompleted < today)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var retention = scope.ServiceProvider.GetRequiredService<AnalyticsRetention>();
                    var days = Math.Max(options.Value.EventRetentionDays, 1);
                    await retention.PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-days), stoppingToken);
                    lastCompleted = today;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogRetentionFailed(logger, ex);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "analytics retention purge failed")]
    private static partial void LogRetentionFailed(ILogger logger, Exception ex);
}
