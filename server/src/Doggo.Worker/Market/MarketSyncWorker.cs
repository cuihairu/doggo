using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Worker.Market;

/// <summary>
/// Periodically pulls market listings from vapor and persists them through
/// <see cref="MarketStore"/>. While vapor is unreachable or misconfigured the loop backs off
/// exponentially instead of crashing; readers keep serving the latest stored snapshots, which
/// is the degradation path for the market API.
/// </summary>
public sealed partial class MarketSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MarketSyncOptions> options,
    ILogger<MarketSyncWorker> logger) : BackgroundService
{
    private int _consecutiveFailures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var syncOptions = options.Value;
        if (!syncOptions.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, syncOptions.IntervalSeconds, syncOptions.AppIds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var synced = false;
                foreach (var appId in syncOptions.AppIds)
                {
                    synced |= await SyncAppAsync(appId, syncOptions, stoppingToken).ConfigureAwait(false);
                }

                if (synced)
                {
                    _consecutiveFailures = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                LogRunFailed(logger, ex.Message, _consecutiveFailures);
            }

            var delay = NextDelay(syncOptions);
            if (_consecutiveFailures > 0)
            {
                LogNextAttempt(logger, delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        LogStopped(logger);
    }

    /// <summary>Runs one full (paged) sync for an app; returns false when vapor was skipped or failed.</summary>
    private async Task<bool> SyncAppAsync(string appId, MarketSyncOptions syncOptions, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<VaporClient>();
        if (!client.IsConfigured)
        {
            LogVaporNotConfigured(logger);
            return false;
        }

        var store = scope.ServiceProvider.GetRequiredService<MarketStore>();

        for (var page = 0; page < Math.Max(syncOptions.MaxPagesPerRun, 1); page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var start = (long)page * syncOptions.PageSize;
            var listings = await client
                .GetMarketListingsAsync(appId, start, syncOptions.PageSize, cancellationToken)
                .ConfigureAwait(false);

            await store.ApplyListingsAsync(appId, listings.Listings, cancellationToken).ConfigureAwait(false);

            if (!listings.HasMore || listings.Listings.Count == 0)
            {
                break;
            }
        }

        return true;
    }

    /// <summary>Healthy interval, doubled per consecutive failure and capped at MaxBackoffSeconds.</summary>
    private TimeSpan NextDelay(MarketSyncOptions syncOptions)
    {
        if (_consecutiveFailures == 0)
        {
            return TimeSpan.FromSeconds(Math.Max(syncOptions.IntervalSeconds, 1));
        }

        var backoff = Math.Max(syncOptions.IntervalSeconds, 1) * (int)Math.Pow(2, Math.Min(_consecutiveFailures, 10));
        return TimeSpan.FromSeconds(Math.Min(backoff, Math.Max(syncOptions.MaxBackoffSeconds, 1)));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "market sync disabled by configuration")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "market sync started: interval {Interval}s, apps {AppIds}")]
    private static partial void LogStarted(ILogger logger, int interval, string[] appIds);

    [LoggerMessage(Level = LogLevel.Information, Message = "market sync stopped")]
    private static partial void LogStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor is not configured; market sync idle until Vapor:BaseUrl and Vapor:ApiKey are set")]
    private static partial void LogVaporNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "market sync run failed ({Failures} consecutive): {Message}")]
    private static partial void LogRunFailed(ILogger logger, string message, int failures);

    [LoggerMessage(Level = LogLevel.Information, Message = "market sync backing off; next attempt in {Delay}")]
    private static partial void LogNextAttempt(ILogger logger, TimeSpan delay);
}
