using Doggo.Domain.Products;
using Doggo.Domain.Price;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.PriceCrawl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

/// <summary>The EF store implementations behind the crawler contracts.</summary>
public class PriceCrawlStoresTests
{
    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"price-crawl-stores-{Guid.NewGuid():N}")
            .Options);

    private static PriceCrawlItemMap NewMap(string siteKey, string externalId,
        PriceMapStatus status = PriceMapStatus.Unconfirmed) => new()
    {
        Id = Guid.CreateVersion7(),
        SiteKey = siteKey,
        ExternalId = externalId,
        ExternalTitle = $"{externalId} title",
        ExternalUrl = $"https://example.test/{externalId}",
        MapStatus = status,
        MatchSource = PriceMapSource.Auto
    };

    // ---- PriceCrawlMapStore ----

    [Fact]
    public async Task GetOrAdd_adds_an_unconfirmed_map_once_then_reuses_it()
    {
        var db = CreateDb();
        var store = new PriceCrawlMapStore(db);

        var first = await store.GetOrAddAsync("buff", "25036", "AK-47 Redline", "https://example.test/25036", CancellationToken.None);
        Assert.Equal(PriceMapStatus.Unconfirmed, first.MapStatus);
        Assert.Equal(PriceMapSource.Auto, first.MatchSource);
        Assert.Equal("buff", first.SiteKey);

        var second = await store.GetOrAddAsync("buff", "25036", "AK-47 Redline", "https://example.test/25036", CancellationToken.None);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.PriceCrawlItemMaps.CountAsync());
    }

    [Fact]
    public async Task FindAsync_returns_null_for_an_unknown_external_id()
    {
        var db = CreateDb();
        db.PriceCrawlItemMaps.Add(NewMap("buff", "25036"));
        await db.SaveChangesAsync();
        var store = new PriceCrawlMapStore(db);

        var known = await store.FindAsync("buff", "25036", CancellationToken.None);
        Assert.NotNull(known);

        Assert.Null(await store.FindAsync("buff", "unknown", CancellationToken.None));
        Assert.Null(await store.FindAsync("other-site", "25036", CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_filters_by_status_and_pages()
    {
        var db = CreateDb();
        db.PriceCrawlItemMaps.AddRange(
            NewMap("buff", "a", PriceMapStatus.Confirmed),
            NewMap("buff", "b", PriceMapStatus.Unconfirmed),
            NewMap("buff", "c", PriceMapStatus.Confirmed));
        await db.SaveChangesAsync();
        var store = new PriceCrawlMapStore(db);

        var confirmed = await store.ListAsync(PriceMapStatus.Confirmed, 0, 10, CancellationToken.None);
        Assert.Equal(2, confirmed.Count);
        Assert.All(confirmed, m => Assert.Equal(PriceMapStatus.Confirmed, m.MapStatus));

        var paged = await store.ListAsync(null, 1, 1, CancellationToken.None);
        Assert.Single(paged);
    }

    [Fact]
    public async Task ListBySiteAsync_scopes_to_one_site()
    {
        var db = CreateDb();
        db.PriceCrawlItemMaps.AddRange(
            NewMap("buff", "a"),
            NewMap("buff", "b"),
            NewMap("igxe", "a"));
        await db.SaveChangesAsync();
        var store = new PriceCrawlMapStore(db);

        var rows = await store.ListBySiteAsync("buff", 10, CancellationToken.None);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, m => Assert.Equal("buff", m.SiteKey));
    }

    [Fact]
    public async Task GetAsync_returns_the_tracked_map()
    {
        var db = CreateDb();
        var map = NewMap("buff", "25036");
        db.PriceCrawlItemMaps.Add(map);
        await db.SaveChangesAsync();
        var store = new PriceCrawlMapStore(db);

        var found = await store.GetAsync(map.Id, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(map.Id, found.Id);
        Assert.Null(await store.GetAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    // ---- PriceCrawlRawStore ----

    [Fact]
    public async Task Raw_add_assigns_the_id_and_persists()
    {
        var db = CreateDb();
        var store = new PriceCrawlRawStore(db, NullLogger<PriceCrawlRawStore>.Instance);
        var raw = new PriceCrawlRaw
        {
            SiteKey = "buff",
            ExternalId = "25036",
            PriceCents = 5200,
            CurrencyCode = "CNY",
            RawPriceText = "¥52",
            ObservedAtUtc = DateTime.UtcNow,
            FetchedAtUtc = DateTime.UtcNow
        };

        await store.AddAsync(raw, CancellationToken.None);
        Assert.NotEqual(Guid.Empty, raw.Id);

        var reloaded = await db.PriceCrawlRaws.SingleAsync();
        Assert.Equal(raw.Id, reloaded.Id);
        Assert.Equal(5200, reloaded.PriceCents);
    }

    [Fact]
    public async Task Raw_purge_removes_only_rows_before_the_cutoff_and_returns_the_count()
    {
        var db = CreateDb();
        var store = new PriceCrawlRawStore(db, NullLogger<PriceCrawlRawStore>.Instance);
        var now = DateTime.UtcNow;
        foreach (var fetched in new[] { now.AddDays(-2), now.AddDays(-3), now.AddHours(-1) })
        {
            await store.AddAsync(new PriceCrawlRaw
            {
                SiteKey = "buff",
                ExternalId = "25036",
                CurrencyCode = "CNY",
                ObservedAtUtc = fetched,
                FetchedAtUtc = fetched
            }, CancellationToken.None);
        }

        var deleted = await store.DeleteOlderThanAsync(now.AddDays(-1), CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal(1, await db.PriceCrawlRaws.CountAsync());
        var survivor = await db.PriceCrawlRaws.SingleAsync();
        Assert.True(survivor.FetchedAtUtc > now.AddDays(-1));
    }

    // ---- PriceCrawlSeriesStore ----

    private static PriceCrawlSeries Point(Guid mapId, string window, long priceCents,
        DateTime observedAtUtc, long? convertedCents = null) => new()
    {
        Id = Guid.CreateVersion7(),
        MapId = mapId,
        MarketItemId = Guid.CreateVersion7(),
        SiteKey = "buff",
        ExternalId = "25036",
        PriceCents = priceCents,
        CurrencyCode = "USD",
        ConvertedCents = convertedCents,
        ExchangeRate = 7.2m,
        ObservedAtUtc = observedAtUtc,
        WindowKey = window
    };

    [Fact]
    public async Task Upsert_adds_the_first_point_then_replaces_a_newer_one_in_the_window()
    {
        var db = CreateDb();
        var store = new PriceCrawlSeriesStore(db);
        var mapId = Guid.CreateVersion7();
        var at10 = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var at12 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(await store.UpsertWindowedAsync(Point(mapId, "2026-11-10", 5200, at10), CancellationToken.None));

        var newer = Point(mapId, "2026-11-10", 5300, at12, convertedCents: 38100);
        Assert.True(await store.UpsertWindowedAsync(newer, CancellationToken.None));

        var kept = await db.PriceCrawlSeriesPoints.SingleAsync();
        Assert.Equal(5300, kept.PriceCents);
        Assert.Equal(38100, kept.ConvertedCents);
        Assert.Equal(at12, kept.ObservedAtUtc);
        Assert.NotEqual(newer.Id, kept.Id);
    }

    [Fact]
    public async Task Upsert_keeps_the_existing_point_when_the_new_one_is_older()
    {
        var db = CreateDb();
        var store = new PriceCrawlSeriesStore(db);
        var mapId = Guid.CreateVersion7();
        var at10 = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var at12 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(await store.UpsertWindowedAsync(Point(mapId, "2026-11-10", 5300, at12), CancellationToken.None));
        Assert.False(await store.UpsertWindowedAsync(Point(mapId, "2026-11-10", 5200, at10), CancellationToken.None));

        var kept = await db.PriceCrawlSeriesPoints.SingleAsync();
        Assert.Equal(5300, kept.PriceCents);
        Assert.Equal(at12, kept.ObservedAtUtc);
    }

    [Fact]
    public async Task RecentCompare_falls_back_to_price_cents_when_unconverted()
    {
        var db = CreateDb();
        var store = new PriceCrawlSeriesStore(db);
        var mapId = Guid.CreateVersion7();
        var baseTime = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        db.PriceCrawlSeriesPoints.AddRange(
            Point(mapId, "w1", 100, baseTime, convertedCents: 720),
            Point(mapId, "w2", 200, baseTime.AddMinutes(1)),
            Point(mapId, "w3", 300, baseTime.AddMinutes(2), convertedCents: 2160));
        await db.SaveChangesAsync();

        var values = await store.RecentCompareCentsAsync(mapId, 2, CancellationToken.None);

        Assert.Equal([2160, 200], values);
    }

    [Fact]
    public async Task QueryByMarketItem_filters_the_window_and_sorts_ascending()
    {
        var db = CreateDb();
        var store = new PriceCrawlSeriesStore(db);
        var mapId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        var otherItemId = Guid.CreateVersion7();
        var from = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        var inRange1 = Point(mapId, "w1", 100, from.AddHours(2));
        var inRange2 = Point(mapId, "w2", 200, from.AddHours(5));
        var tooEarly = Point(mapId, "w0", 50, from.AddHours(-2));
        inRange1.MarketItemId = itemId;
        inRange2.MarketItemId = itemId;
        tooEarly.MarketItemId = itemId;
        var otherItem = Point(Guid.CreateVersion7(), "w1", 900, from.AddHours(3));
        otherItem.MarketItemId = otherItemId;
        db.PriceCrawlSeriesPoints.AddRange(inRange1, inRange2, tooEarly, otherItem);
        await db.SaveChangesAsync();

        var rows = await store.QueryByMarketItemAsync(itemId, from, to, CancellationToken.None);

        Assert.Equal([inRange1.Id, inRange2.Id], rows.Select(p => p.Id));
    }

    // ---- PriceCrawlSiteStateStore ----

    [Fact]
    public async Task Site_state_get_or_add_creates_once_and_save_persists_the_counters()
    {
        var db = CreateDb();
        var store = new PriceCrawlSiteStateStore(db);

        var state = await store.GetOrAddAsync("buff", CancellationToken.None);
        Assert.Equal(0, state.ConsecutiveFailures);

        state.ConsecutiveFailures = 2;
        state.LastError = "robots fetch failed";
        await store.SaveAsync(state, CancellationToken.None);

        var second = await store.GetOrAddAsync("buff", CancellationToken.None);
        Assert.Equal(state.Id, second.Id);
        Assert.Equal(2, second.ConsecutiveFailures);
        Assert.Equal(1, await db.PriceCrawlSiteStates.CountAsync());
    }

    // ---- ExchangeRateReader ----

    [Fact]
    public async Task Rate_reader_treats_cny_as_one_without_a_lookup()
    {
        var store = new ExchangeRateReader(CreateDb());
        Assert.Equal(1m, await store.GetRateToCnyAsync("CNY", CancellationToken.None));
        Assert.Equal(1m, await store.GetRateToCnyAsync(" cny ", CancellationToken.None));
    }

    [Fact]
    public async Task Rate_reader_reads_the_rate_by_trimmed_upper_code()
    {
        var db = CreateDb();
        db.ExchangeRates.Add(new ExchangeRate
        {
            Id = Guid.CreateVersion7(),
            CurrencyCode = "USD",
            Rate = 7.25m,
            Source = "manual"
        });
        await db.SaveChangesAsync();
        var store = new ExchangeRateReader(db);

        Assert.Equal(7.25m, await store.GetRateToCnyAsync(" usd ", CancellationToken.None));
        Assert.Null(await store.GetRateToCnyAsync("EUR", CancellationToken.None));
    }
}
