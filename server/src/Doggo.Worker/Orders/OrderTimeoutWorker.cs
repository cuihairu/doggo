using Doggo.Infrastructure.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Orders;

/// <summary>Cancels unpaid orders whose payment deadline has passed.</summary>
public sealed partial class OrderTimeoutWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderOptions> options,
    ILogger<OrderTimeoutWorker> logger) : BackgroundService
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
                var expired = await orders.ExpireUnpaidOrdersAsync(cancellationToken: stoppingToken);
                if (expired > 0)
                {
                    LogExpiredBatch(logger, expired);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "timeout sweep cancelled {Count} unpaid orders")]
    private static partial void LogExpiredBatch(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "order timeout sweep failed")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
