using Doggo.Infrastructure.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Orders;

/// <summary>
/// Compensation sweeps: refunds paid orders stuck before settlement/retrieval, and rolls
/// retrievals that never finished back so they can be retried or refunded.
/// </summary>
public sealed partial class OrderReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderOptions> options,
    ILogger<OrderReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.WorkerIntervalSeconds, 5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var orders = scope.ServiceProvider.GetRequiredService<OrderService>();

                var refunded = await orders.RefundStuckOrdersAsync(cancellationToken: stoppingToken);
                if (refunded > 0)
                {
                    LogRefundedBatch(logger, refunded);
                }

                var rolled = await orders.RollbackStuckRetrievalsAsync(cancellationToken: stoppingToken);
                if (rolled > 0)
                {
                    LogRolledBatch(logger, rolled);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSweepFailed(logger, ex);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "reconciliation refunded {Count} stuck orders")]
    private static partial void LogRefundedBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "reconciliation rolled back {Count} stuck retrievals")]
    private static partial void LogRolledBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "order reconciliation sweep failed")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
