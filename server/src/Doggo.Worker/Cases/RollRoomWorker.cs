using Doggo.Infrastructure.Cases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Cases;

/// <summary>Draws due roll rooms and rolls finished periods into the next one.</summary>
public sealed partial class RollRoomWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RollOptions> options,
    ILogger<RollRoomWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(options.Value.WorkerIntervalSeconds, 5));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var rolls = scope.ServiceProvider.GetRequiredService<RollService>();
                var drawn = await rolls.DrawDueRoomsAsync(stoppingToken);
                var rolled = await rolls.OpenNextPeriodsAsync(stoppingToken);
                if (drawn > 0 || rolled > 0)
                {
                    LogSweep(logger, drawn, rolled);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "roll sweep drew {Drawn} rooms and rolled {Rolled} periods")]
    private static partial void LogSweep(ILogger logger, int drawn, int rolled);

    [LoggerMessage(Level = LogLevel.Error, Message = "roll room sweep failed")]
    private static partial void LogSweepFailed(ILogger logger, Exception ex);
}
