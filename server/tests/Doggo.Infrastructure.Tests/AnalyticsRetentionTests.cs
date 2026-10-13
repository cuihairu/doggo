using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

/// <summary>Event-detail retention: rows past the cutoff are purged, stat rows are not (funnel design §10 #9).</summary>
public class AnalyticsRetentionTests
{
    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"analytics-retention-{Guid.NewGuid():N}")
            .Options);

    private static AnalyticsRetention NewRetention(DoggoDbContext db) =>
        new(db, NullLogger<AnalyticsRetention>.Instance);

    private static DateTime Ago(int days) => DateTime.UtcNow.AddDays(-days);

    private static AnalyticsEvent Evt(DateTime occurred, DateTime received, string name = "page_view_product") => new()
    {
        Id = Guid.CreateVersion7(),
        EventName = name,
        OccurredAtUtc = occurred,
        ReceivedAtUtc = received,
        SessionId = Guid.NewGuid(),
        Client = AnalyticsClient.Web,
        PropertiesJson = "{}",
        IdempotencyKey = $"{name}:{Guid.NewGuid():N}"
    };

    private static FunnelDailyStat Stat(DateOnly day) => new() { Date = day, Client = AnalyticsClient.Web };

    [Fact]
    public async Task Purges_events_past_cutoff_and_keeps_recent_rows()
    {
        var db = CreateDb();
        db.AnalyticsEvents.AddRange(
            Evt(Ago(91), Ago(91)),
            Evt(Ago(200), Ago(120)),
            Evt(Ago(10), Ago(10)));
        db.FunnelDailyStats.Add(Stat(new DateOnly(2026, 10, 5)));
        await db.SaveChangesAsync();

        var deleted = await NewRetention(db).PurgeOlderThanAsync(Ago(90));

        Assert.Equal(2, deleted);
        var remaining = await db.AnalyticsEvents.ToListAsync();
        Assert.Single(remaining);
        Assert.True(remaining[0].OccurredAtUtc > Ago(90));
        Assert.Single(db.FunnelDailyStats);
    }

    [Fact]
    public async Task Keeps_rows_that_are_old_by_only_one_measure()
    {
        var db = CreateDb();
        db.AnalyticsEvents.AddRange(
            // Old event, still in flight: not safe to drop, late catch-up may still be aggregated.
            Evt(Ago(200), Ago(0)),
            // Long-delayed ingest of an event that only just became relevant.
            Evt(Ago(0), Ago(200)));
        await db.SaveChangesAsync();

        var deleted = await NewRetention(db).PurgeOlderThanAsync(Ago(90));

        Assert.Equal(0, deleted);
        Assert.Equal(2, await db.AnalyticsEvents.CountAsync());
    }

    [Fact]
    public async Task Is_idempotent_when_nothing_is_left_to_purge()
    {
        var db = CreateDb();
        db.AnalyticsEvents.Add(Evt(Ago(10), Ago(10)));
        await db.SaveChangesAsync();

        var deleted = await NewRetention(db).PurgeOlderThanAsync(Ago(90));

        Assert.Equal(0, deleted);
        Assert.Single(db.AnalyticsEvents);
    }

    [Fact]
    public async Task Batched_purge_deletes_rows_beyond_one_batch()
    {
        var db = CreateDb();
        var rows = Enumerable.Range(0, 1200)
            .Select(_ => Evt(Ago(120), Ago(120)))
            .ToList();
        db.AnalyticsEvents.AddRange(rows);
        await db.SaveChangesAsync();

        var deleted = await NewRetention(db).PurgeOlderThanAsync(Ago(90));

        Assert.Equal(1200, deleted);
        Assert.Equal(0, await db.AnalyticsEvents.CountAsync());
    }

    [Fact]
    public void Default_retention_is_90_days()
    {
        Assert.Equal(90, new AnalyticsOptions().EventRetentionDays);
    }
}
