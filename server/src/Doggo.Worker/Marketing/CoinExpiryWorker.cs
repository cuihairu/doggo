using Doggo.Infrastructure.Marketing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Marketing;

/// <summary>Sweeps expired coin batches so balances stop counting dead coins.</summary>
public sealed partial class CoinExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CoinOptions> options,
    ILogger<CoinExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.SweepIntervalSeconds, 10));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coins = scope.ServiceProvider.GetRequiredService<CoinService>();
                var expired = await coins.ExpireDueBatchesAsync(stoppingToken);
                if (expired > 0)
                {
                    LogSweep(logger, expired);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "coin expiry sweep zeroed {Batches} batches")]
    private static partial void LogSweep(ILogger logger, int batches);

    [LoggerMessage(Level = LogLevel.Error, Message = "coin expiry sweep failed")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
