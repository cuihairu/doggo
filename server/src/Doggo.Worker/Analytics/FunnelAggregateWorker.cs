using Doggo.Infrastructure.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Doggo.Worker.Analytics;

/// <summary>
/// Daily T-1 aggregation plus a rolling T-7 recompute so late catch-up events and payment
/// callbacks are absorbed (funnel design §7.4). Recompute is idempotent: reruns overwrite.
/// </summary>
public sealed partial class FunnelAggregateWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FunnelAggregateWorker> logger) : BackgroundService
{
    private static readonly TimeOnly DueAt = new(0, 30);

    private const int RecomputeDays = 7;

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
                    var aggregator = scope.ServiceProvider.GetRequiredService<FunnelAggregator>();
                    var rows = await aggregator.AggregateRangeAsync(
                        today.AddDays(-RecomputeDays), today.AddDays(-1), stoppingToken);
                    lastCompleted = today;
                    LogAggregated(logger, rows);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogAggregationFailed(logger, ex);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "funnel aggregation wrote {Rows} stat rows")]
    private static partial void LogAggregated(ILogger logger, int rows);

    [LoggerMessage(Level = LogLevel.Error, Message = "funnel aggregation failed")]
    private static partial void LogAggregationFailed(ILogger logger, Exception ex);
}
