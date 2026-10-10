namespace Doggo.Worker;

/// <summary>
/// Background job host placeholder. Scheduled jobs (order expiry, price sync,
/// roll-room draws, coin expiry, compensation runs) land here in milestones M2-M6.
/// </summary>
public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started (no jobs registered yet)");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
