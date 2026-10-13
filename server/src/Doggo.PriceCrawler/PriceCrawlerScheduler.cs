using Doggo.Domain.Price;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler;

/// <summary>
/// Per-site run loop (design §5): due check → circuit breaker → robots gate → discovery
/// cadence → price fetch → ingest. Sites run sequentially; the global concurrency budget
/// (GlobalConcurrentFetches) is respected by staying at 1 in-flight adapter call per site tick.
/// </summary>
public sealed class PriceCrawlerScheduler(
    SiteAdapterRegistry adapters,
    IPriceCrawlMapStore maps,
    IPriceCrawlRawStore raws,
    IPriceCrawlSeriesStore series,
    IPriceCrawlSiteStateStore siteStates,
    IExchangeRateReader exchangeRates,
    Robots.RobotsChecker robots,
    IOptions<PriceCrawlerOptions> options,
    ILogger<PriceCrawlerScheduler> logger)
{
    private readonly PriceCrawlerOptions _options = options.Value;
    private readonly PriceCrawlIngestor _ingestor = new(maps, raws, series, exchangeRates, options, logger);

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (InNightWindow(DateTime.UtcNow))
        {
            logger.LogDebug("Price crawl paused in night window {Start}-{End} UTC",
                _options.NightWindowUtc.Start, _options.NightWindowUtc.End);
            return;
        }

        await PurgeRawAsync(cancellationToken).ConfigureAwait(false);

        foreach (var site in _options.Sites.Where(s => s.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!adapters.TryGet(site.Key, out var adapter))
            {
                logger.LogWarning("No adapter registered for price site {SiteKey}", site.Key);
                continue;
            }

            try
            {
                await RunSiteAsync(site, adapter, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Price site {SiteKey} run failed", site.Key);
            }
        }
    }

    private async Task RunSiteAsync(PriceCrawlerSiteOptions site, ISiteAdapter adapter, CancellationToken cancellationToken)
    {
        var state = await siteStates.GetOrAddAsync(site.Key, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;

        if (state.CircuitOpenUntilUtc is { } openUntil && openUntil > now)
        {
            logger.LogDebug("Price site {SiteKey} circuit open until {Until} UTC", site.Key, openUntil);
            return;
        }

        var intervalMinutes = Math.Max(site.IntervalMinutes, 1);
        if (state.RobotsCrawlDelaySeconds > 0)
        {
            intervalMinutes = Math.Max(intervalMinutes, (int)Math.Ceiling(state.RobotsCrawlDelaySeconds / 60m));
        }

        if (state.LastRunAtUtc is { } lastRun && lastRun.AddMinutes(intervalMinutes) > now)
        {
            return;
        }

        var decision = await robots.IsAllowedAsync(adapter.RobotsUrl, site.RobotsEnabled, site.IntervalMinutes, cancellationToken)
            .ConfigureAwait(false);
        if (!decision.Allowed)
        {
            logger.LogInformation("Price site {SiteKey} disallowed by robots.txt; skipping", site.Key);
            return;
        }

        state.RobotsCrawlDelaySeconds = decision.CrawlDelaySeconds ?? 0;

        var maxItems = site.MaxItemsPerRun ?? _options.MaxItemsPerRun;
        try
        {
            await RunDiscoveryAsync(site, adapter, state, cancellationToken).ConfigureAwait(false);

            var knownMaps = await maps.ListBySiteAsync(site.Key, maxItems, cancellationToken).ConfigureAwait(false);
            var ids = knownMaps
                .Where(m => m.MapStatus != PriceMapStatus.Rejected)
                .Select(m => m.ExternalId)
                .Take(maxItems)
                .ToArray();

            if (ids.Length > 0)
            {
                var result = await PriceCrawlRetry.ExecuteAsync(
                    _options.Retry,
                    (_, token) => _ingestor.IngestAsync(adapter, ids, token),
                    cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Price site {SiteKey}: fetched {Fetched}, raw {Raw}, series {Series} (suspect {Suspect}, unconfirmed {Unconfirmed})",
                    site.Key, result.Fetched, result.RawStored, result.SeriesUpserted, result.Suspect, result.SkippedUnconfirmed);
            }

            state.LastRunAtUtc = DateTime.UtcNow;
            state.ConsecutiveFailures = 0;
            state.LastError = string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.ConsecutiveFailures++;
            state.LastError = ex.Message;
            if (state.ConsecutiveFailures >= _options.CircuitBreaker.FailureThreshold)
            {
                state.CircuitOpenUntilUtc = DateTime.UtcNow.AddMinutes(_options.CircuitBreaker.CooldownMinutes);
                state.ConsecutiveFailures = 0;
                logger.LogError("Price site {SiteKey} opened circuit for {Minutes}min after repeated failures",
                    site.Key, _options.CircuitBreaker.CooldownMinutes);
            }
        }
        finally
        {
            await siteStates.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Discovery runs once per IntervalHours at/after RunAtUtc (default daily 04:00 UTC).</summary>
    private async Task RunDiscoveryAsync(
        PriceCrawlerSiteOptions site,
        ISiteAdapter adapter,
        PriceCrawlSiteState state,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(_options.Discover.IntervalHours, 1));
        if (state.LastDiscoverAtUtc is { } last && last.Add(interval) > DateTime.UtcNow)
        {
            return;
        }

        if (!TimeOfDayReached(_options.Discover.RunAtUtc))
        {
            return;
        }

        foreach (var hashName in site.MarketHashNames.DefaultIfEmpty(string.Empty))
        {
            var query = new DiscoveryQuery(site.MarketHashNames.Count > 0 ? hashName : null, null, hashName.Length > 0 ? hashName : null);
            var refs = await PriceCrawlRetry.ExecuteAsync(
                _options.Retry,
                (_, token) => adapter.DiscoverAsync(query, token),
                cancellationToken).ConfigureAwait(false);

            foreach (var reference in refs)
            {
                await maps.GetOrAddAsync(site.Key, reference.ExternalId, reference.Title, reference.DetailUrl, cancellationToken)
                    .ConfigureAwait(false);
            }

            logger.LogInformation("Price site {SiteKey} discovery added {Count} refs for {Query}",
                site.Key, refs.Count, query.MarketHashName ?? "(all)");
        }

        state.LastDiscoverAtUtc = DateTime.UtcNow;
    }

    private async Task PurgeRawAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RawRetentionDays);
        var deleted = await raws.DeleteOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} raw price rows older than {Cutoff:yyyy-MM-dd}", deleted, cutoff);
        }
    }

    private bool InNightWindow(DateTime utcNow)
    {
        if (!_options.NightWindowUtc.Enabled)
        {
            return false;
        }

        var now = utcNow.TimeOfDay;
        var start = ParseTime(_options.NightWindowUtc.Start);
        var end = ParseTime(_options.NightWindowUtc.End);
        return start <= end ? now >= start && now < end : now >= start || now < end;
    }

    private static bool TimeOfDayReached(string hhmm)
    {
        var target = ParseTime(hhmm);
        return DateTime.UtcNow.TimeOfDay >= target;
    }

    private static TimeSpan ParseTime(string hhmm)
    {
        var parts = hhmm.Split(':');
        var hours = parts.Length > 0 && int.TryParse(parts[0], out var h) ? Math.Clamp(h, 0, 23) : 0;
        var minutes = parts.Length > 1 && int.TryParse(parts[1], out var m) ? Math.Clamp(m, 0, 59) : 0;
        return new TimeSpan(hours, minutes, 0);
    }
}
