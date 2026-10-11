using Doggo.Domain.News;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

public class MarketStoreTests
{
    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"market-tests-{Guid.NewGuid():N}")
            .Options);

    private static MarketStore CreateStore(DoggoDbContext db) => new(db, NullLogger<MarketStore>.Instance);

    private static VaporMarketListingView Listing(
        string hashName,
        string? classId,
        decimal? price,
        int minutesAgo = 0,
        int? currencyId = 23,
        string appId = "730") =>
        new(Guid.NewGuid(), hashName, hashName, 5, appId, null, classId, null, price, currencyId,
            DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    [Fact]
    public async Task Apply_creates_item_change_only_snapshots_and_daily_fold()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        var result = await store.ApplyListingsAsync("730",
        [
            Listing("Case A", "101", 10.00m, minutesAgo: 60),
            Listing("Case A", "101", 12.50m, minutesAgo: 30),
            Listing("Case A", "101", 9.00m, minutesAgo: 5)
        ]);

        var item = db.MarketItems.Single();
        Assert.Equal("101", item.ClassId);
        Assert.Equal("Case A", item.MarketHashName);
        Assert.Equal(GameKind.Cs2, item.Game);

        Assert.Equal(3, db.MarketPriceSnapshots.Count());
        Assert.Equal(3, result.SnapshotsAdded);

        var daily = db.MarketPriceDailies.Single();
        Assert.Equal(1000, daily.OpenCents);
        Assert.Equal(1250, daily.HighCents);
        Assert.Equal(900, daily.LowCents);
        Assert.Equal(900, daily.CloseCents);

        Assert.Equal(3, result.ItemsTouched);
        Assert.False(string.IsNullOrEmpty(db.SyncCursors.Single().Cursor));
    }

    [Fact]
    public async Task Apply_skips_unchanged_prices()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        await store.ApplyListingsAsync("730", [Listing("Case A", "101", 10.00m)]);
        var afterFirst = db.MarketPriceSnapshots.Count();

        var second = await store.ApplyListingsAsync("730", [Listing("Case A", "101", 10.00m)]);

        Assert.Equal(afterFirst, db.MarketPriceSnapshots.Count());
        Assert.Equal(0, second.SnapshotsAdded);
    }

    [Fact]
    public async Task Apply_merges_aggregate_into_classid_row()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        // Search aggregates arrive without a class id; the class-id listing for the same
        // item must land on the same row, with the class id filled in.
        var result = await store.ApplyListingsAsync("730",
        [
            Listing("Case A", null, 10.00m),
            Listing("Case A", "101", 11.00m)
        ]);

        var item = db.MarketItems.Single();
        Assert.Equal("101", item.ClassId);
        Assert.Equal(2, result.ItemsTouched);
        Assert.Equal(2, db.MarketPriceSnapshots.Count());
    }

    [Fact]
    public async Task Apply_skips_listings_without_price()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        var result = await store.ApplyListingsAsync("730", [Listing("Case A", "101", null)]);

        Assert.Single(db.MarketItems);
        Assert.Empty(db.MarketPriceSnapshots);
        Assert.Equal(0, result.SnapshotsAdded);
    }

    [Fact]
    public async Task Apply_maps_games_and_currencies()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        await store.ApplyListingsAsync("570", [Listing("Treasure", null, 6.00m, currencyId: 23, appId: "570")]);
        await store.ApplyListingsAsync("730", [Listing("Case B", "102", 2.00m, currencyId: 1)]);

        Assert.Equal(GameKind.Dota2, db.MarketItems.Single(i => i.MarketHashName == "Treasure").Game);
        Assert.Equal(GameKind.Cs2, db.MarketItems.Single(i => i.MarketHashName == "Case B").Game);

        var usd = db.MarketPriceSnapshots.Single(s => s.Currency == "USD");
        Assert.Equal(200, usd.PriceCents);
        Assert.All(db.MarketPriceSnapshots.Where(s => s.Currency == "CNY"), s => Assert.Equal(600, s.PriceCents));
    }

    [Fact]
    public async Task Apply_folds_into_one_daily_row_per_item_per_day()
    {
        using var db = CreateDb();
        var store = CreateStore(db);

        await store.ApplyListingsAsync("730",
        [
            Listing("Case A", "101", 10.00m, minutesAgo: 60),
            Listing("Case A", "101", 11.00m),
            Listing("Case B", "102", 5.00m)
        ]);

        Assert.Equal(2, db.MarketPriceDailies.Count());
        var caseADaily = db.MarketPriceDailies.Single(d => d.CloseCents == 1100);
        Assert.Equal(1000, caseADaily.OpenCents);
    }
}
