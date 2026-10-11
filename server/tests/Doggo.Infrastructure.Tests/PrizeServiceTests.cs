using Doggo.Domain.Cases;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

public class PrizeServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"prize-tests-{Guid.NewGuid():N}")
            .Options);

    private static PrizeService CreateService(DoggoDbContext db) =>
        new(db);

    private static UserPrize Prize(string marketHashName = "AK-47 | Redline (Field-Tested)", Guid? orderId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = UserId,
        OrderId = orderId ?? Guid.CreateVersion7(),
        Source = PrizeSource.GroupOpen,
        AssetId = Guid.CreateVersion7().ToString("N"),
        ClassId = "3250",
        InstanceId = "0",
        MarketHashName = marketHashName,
        Quality = CaseQuality.Classified,
        Exterior = "Field-Tested",
        FloatValue = 0.2312,
        Rarity = "classified",
        IconUrl = "https://img.example/ak.png",
        PriceCents = 8800
    };

    private static async Task<MarketItem> SeedMarketItemAsync(DoggoDbContext db, string marketHashName)
    {
        var item = new MarketItem
        {
            Id = Guid.CreateVersion7(),
            ClassId = "3250",
            MarketHashName = marketHashName,
            ItemType = MarketItemType.Skin,
            Game = GameKind.Cs2,
            IconUrl = "https://img.example/ak.png"
        };
        db.MarketItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task List_returns_pages_ordered_by_newest()
    {
        using var db = CreateDb();
        var newest = Prize();
        var middle = Prize();
        var oldest = Prize();
        db.UserPrizes.AddRange(newest, middle, oldest);
        await db.SaveChangesAsync();
        // The audit pipeline stamps CreatedAtUtc at save time in insertion order; rewrite
        // them explicitly so the intended order is unambiguous.
        newest.CreatedAtUtc = DateTime.UtcNow;
        middle.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-1);
        oldest.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-2);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var page = await service.ListPrizesAsync(UserId, page: 1, pageSize: 2);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(newest.Id, page.Items[0].Id);
        Assert.Equal(middle.Id, page.Items[1].Id);

        var second = await service.ListPrizesAsync(UserId, page: 2, pageSize: 2);
        var secondItem = Assert.Single(second.Items);
        Assert.Equal(oldest.Id, secondItem.Id);
    }

    [Fact]
    public async Task List_links_latest_market_snapshot_and_leaves_unknown_items_unvalued()
    {
        using var db = CreateDb();
        var item = await SeedMarketItemAsync(db, "AK-47 | Redline (Field-Tested)");
        db.MarketPriceSnapshots.AddRange(
            new MarketPriceSnapshot { Id = Guid.CreateVersion7(), MarketItemId = item.Id, PriceCents = 9000, CapturedAtUtc = DateTime.UtcNow.AddHours(-3) },
            new MarketPriceSnapshot { Id = Guid.CreateVersion7(), MarketItemId = item.Id, PriceCents = 9500, CapturedAtUtc = DateTime.UtcNow });
        var valued = Prize();
        var unvalued = Prize("Some Unknown Skin (Battle-Scarred)");
        await db.UserPrizes.AddRangeAsync(valued, unvalued);
        await db.SaveChangesAsync();

        var page = await CreateService(db).ListPrizesAsync(UserId, page: 1, pageSize: 50);

        var valuedView = page.Items.Single(p => p.Id == valued.Id);
        Assert.Equal(9500, valuedView.MarketPriceCents);
        Assert.NotNull(valuedView.MarketPriceAtUtc);
        Assert.Equal(8800, valuedView.PriceCents); // record-time price untouched

        var unvaluedView = page.Items.Single(p => p.Id == unvalued.Id);
        Assert.Null(unvaluedView.MarketPriceCents);
        Assert.Null(unvaluedView.MarketPriceAtUtc);
    }

    [Fact]
    public async Task Detail_is_scoped_to_the_owner()
    {
        using var db = CreateDb();
        var prize = Prize();
        db.UserPrizes.Add(prize);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var own = await service.GetPrizeAsync(UserId, prize.Id);
        Assert.Equal(prize.Id, own.Id);

        await Assert.ThrowsAsync<PrizeNotFoundException>(() =>
            service.GetPrizeAsync(Guid.CreateVersion7(), prize.Id));
        await Assert.ThrowsAsync<PrizeNotFoundException>(() =>
            service.GetPrizeAsync(UserId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Logs_list_only_for_own_prizes()
    {
        using var db = CreateDb();
        var prize = Prize();
        db.UserPrizes.Add(prize);
        db.UserPrizeLogs.AddRange(
            new UserPrizeLog { Id = Guid.CreateVersion7(), PrizeId = prize.Id, UserId = UserId, OrderId = prize.OrderId, Source = prize.Source, RawJson = "{\"a\":1}" },
            new UserPrizeLog { Id = Guid.CreateVersion7(), PrizeId = prize.Id, UserId = UserId, OrderId = prize.OrderId, Source = prize.Source });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var logs = await service.ListLogsAsync(UserId, prize.Id);
        Assert.Equal(2, logs.Count);

        await Assert.ThrowsAsync<PrizeNotFoundException>(() =>
            service.ListLogsAsync(Guid.CreateVersion7(), prize.Id));
        await Assert.ThrowsAsync<PrizeNotFoundException>(() =>
            service.ListLogsAsync(UserId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task List_for_unknown_user_is_an_empty_page()
    {
        using var db = CreateDb();
        var page = await CreateService(db).ListPrizesAsync(Guid.CreateVersion7(), page: 1, pageSize: 20);
        Assert.Equal(0, page.Total);
        Assert.Empty(page.Items);
    }
}
