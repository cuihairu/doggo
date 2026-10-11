using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Scraper.News;

public sealed class ScraperOptions
{
    public const string SectionName = "Scraper";

    /// <summary>How often the scheduler re-checks whether sources are due.</summary>
    public int ScanIntervalSeconds { get; set; } = 300;

    /// <summary>Sources that fail this many times in a row stop being fetched for a cooldown.</summary>
    public int CircuitFailureThreshold { get; set; } = 5;

    /// <summary>Cooldown applied after consecutive failures (minutes).</summary>
    public int CircuitCooldownMinutes { get; set; } = 30;

    /// <summary>Request timeout per source fetch (seconds).</summary>
    public int FetchTimeoutSeconds { get; set; } = 60;

    /// <summary>Base delay between fetch retries; grows linearly per attempt (seconds).</summary>
    public int RetryDelaySeconds { get; set; } = 2;
}

/// <summary>
/// Runs due sources: respects per-source intervals, applies a circuit breaker around
/// repeatedly failing sources, and stores the fetched entries.
/// </summary>
public sealed class ScraperScheduler(
    IServiceScopeFactory scopeFactory,
    SourceAdapterRegistry adapters,
    IOptions<ScraperOptions> options,
    ILogger<ScraperScheduler> logger)
{
    private readonly ScraperOptions _options = options.Value;

    public async Task RunDueSourcesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var sources = await db.NewsSources
            .Where(s => s.Enabled)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        if (sources.Count == 0)
        {
            return;
        }

        logger.LogDebug("Scanning {Count} enabled news sources", sources.Count);
        await Task.WhenAll(sources.Select(s => RunSourceAsync(s, now, cancellationToken)));
    }

    public async Task RunSourceAsync(NewsSource source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var live = await db.NewsSources.FirstOrDefaultAsync(s => s.Id == source.Id, cancellationToken);
        if (live is null)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(live.FetchIntervalMinutes, 1));
        var lastFetch = live.LastFetchedAtUtc;
        if (lastFetch.HasValue && now.UtcDateTime - lastFetch.Value < interval)
        {
            logger.LogDebug("Source {Source} not due yet", live.Name);
            return;
        }

        if (live.ConsecutiveFailures >= _options.CircuitFailureThreshold
            && lastFetch.HasValue
            && now.UtcDateTime - lastFetch.Value < TimeSpan.FromMinutes(_options.CircuitCooldownMinutes))
        {
            logger.LogWarning("Source {Source} circuit open, skipping ({Failures} consecutive failures)",
                live.Name, live.ConsecutiveFailures);
            return;
        }

        var adapter = adapters.Get(live.SourceType);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(_options.FetchTimeoutSeconds, 1)));

            var candidates = await adapter.FetchAsync(live, timeout.Token);
            var store = scope.ServiceProvider.GetRequiredService<NewsStore>();
            var inserted = await store.SaveCandidatesAsync(live, candidates, timeout.Token);

            logger.LogInformation(
                "Source {Source}: fetched {Fetched}, inserted {Inserted}, skipped {Skipped}",
                live.Name, candidates.Count, inserted, candidates.Count - inserted);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Source {Source} fetch timed out after {Seconds}s",
                live.Name, _options.FetchTimeoutSeconds);
            await FailAsync(db, live);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Source {Source} fetch failed", live.Name);
            await FailAsync(db, live);
        }
    }

    private static async Task FailAsync(DoggoDbContext db, NewsSource source)
    {
        db.NewsSources.Update(source);
        source.ConsecutiveFailures++;
        await db.SaveChangesAsync();
    }
}
