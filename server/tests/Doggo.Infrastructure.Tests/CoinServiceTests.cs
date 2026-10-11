using Doggo.Domain.Users;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

public class CoinServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"coin-service-tests-{Guid.NewGuid():N}")
            .Options);

    private static CoinService CreateService(DoggoDbContext db) =>
        new(db, NullLogger<CoinService>.Instance);

    [Fact]
    public async Task Balance_ForUnknownUser_IsZero()
    {
        await using var db = CreateDb();

        var balance = await CreateService(db).GetBalanceAsync(UserId);

        Assert.Equal(0, balance.Balance);
    }

    [Fact]
    public async Task Grant_CreatesAccountBatchAndLog()
    {
        await using var db = CreateDb();

        var balance = await CreateService(db).GrantAsync(UserId, 500, "roll_room", "room-1");

        Assert.Equal(500, balance);
        var batch = await db.UserCoinBatches.SingleAsync();
        Assert.Equal(500, batch.RemainingAmount);
        Assert.Equal("roll_room", batch.SourceType);
        var log = await db.UserCoinLogs.SingleAsync();
        Assert.Equal(CoinLogType.Grant, log.Type);
        Assert.Equal(500, log.BalanceAfter);
    }

    [Fact]
    public async Task Grant_WithNonPositiveAmount_IsRejected()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<CoinValidationException>(
            () => CreateService(db).GrantAsync(UserId, 0, "test", null));
        await Assert.ThrowsAsync<CoinValidationException>(
            () => CreateService(db).GrantAsync(UserId, -5, "test", null));
    }

    [Fact]
    public async Task Spend_ConsumesTheEarliestExpiringBatchFirst()
    {
        await using var db = CreateDb();
        var coins = CreateService(db);
        // Batch A dies sooner, batch B never expires: A must drain first.
        await coins.GrantAsync(UserId, 100, "promo", "a", expiresAtUtc: DateTime.UtcNow.AddHours(1));
        await coins.GrantAsync(UserId, 100, "promo", "b", expiresAtUtc: null);

        var balance = await coins.SpendAsync(UserId, 150, "order", "order-1");

        Assert.Equal(50, balance);
        var batches = await db.UserCoinBatches.OrderBy(b => b.SourceRef).ToListAsync();
        Assert.Equal(0, batches.Single(b => b.SourceRef == "a").RemainingAmount);
        Assert.Equal(50, batches.Single(b => b.SourceRef == "b").RemainingAmount);
        var log = await db.UserCoinLogs.SingleAsync(l => l.Type == CoinLogType.Spend);
        Assert.Equal(-150, log.Delta);
        Assert.Equal(50, log.BalanceAfter);
    }

    [Fact]
    public async Task Spend_WithInsufficientBalance_IsRejectedWithoutPartialDeduction()
    {
        await using var db = CreateDb();
        var coins = CreateService(db);
        await coins.GrantAsync(UserId, 100, "promo", null);

        await Assert.ThrowsAsync<CoinValidationException>(
            () => coins.SpendAsync(UserId, 101, "order", "order-1"));

        Assert.Equal(100, (await db.UserCoins.SingleAsync()).Balance);
        Assert.True(await db.UserCoinBatches.AllAsync(b => b.RemainingAmount == 100));
        Assert.DoesNotContain(await db.UserCoinLogs.ToListAsync(), l => l.Type == CoinLogType.Spend);
    }

    [Fact]
    public async Task Spend_WithoutAnyBalance_IsRejected()
    {
        await using var db = CreateDb();

        await Assert.ThrowsAsync<CoinValidationException>(
            () => CreateService(db).SpendAsync(UserId, 10, "order", "order-1"));
    }

    [Fact]
    public async Task Refund_ComesBackAsANonExpiringBatch()
    {
        await using var db = CreateDb();
        var coins = CreateService(db);

        var balance = await coins.RefundAsync(UserId, 30, "order", "order-9");

        Assert.Equal(30, balance);
        var batch = await db.UserCoinBatches.SingleAsync();
        Assert.Equal("refund", batch.SourceType);
        Assert.Null(batch.ExpiresAtUtc);
    }

    [Fact]
    public async Task ExpireDueBatches_ZeroesOnlyExpiredBatchesAndWritesOneLogPerUser()
    {
        await using var db = CreateDb();
        var coins = CreateService(db);
        var other = Guid.CreateVersion7();
        await coins.GrantAsync(UserId, 80, "promo", "dead", expiresAtUtc: DateTime.UtcNow.AddMinutes(-5));
        await coins.GrantAsync(UserId, 20, "promo", "alive", expiresAtUtc: DateTime.UtcNow.AddDays(1));
        await coins.GrantAsync(other, 40, "promo", "dead-too", expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var expired = await coins.ExpireDueBatchesAsync();

        Assert.Equal(2, expired);
        Assert.Equal(20, (await db.UserCoins.SingleAsync(c => c.UserId == UserId)).Balance);
        Assert.Equal(0, (await db.UserCoins.SingleAsync(c => c.UserId == other)).Balance);
        var batches = await db.UserCoinBatches.ToListAsync();
        Assert.Equal(0, batches.Single(b => b.SourceRef == "dead").RemainingAmount);
        Assert.Equal(20, batches.Single(b => b.SourceRef == "alive").RemainingAmount);
        Assert.Equal(0, batches.Single(b => b.SourceRef == "dead-too").RemainingAmount);
        Assert.Equal(2, (await db.UserCoinLogs.ToListAsync()).Count(l => l.Type == CoinLogType.Expire));
    }

    [Fact]
    public async Task ExpireDueBatches_WithNothingDue_IsANoOp()
    {
        await using var db = CreateDb();
        var coins = CreateService(db);
        await coins.GrantAsync(UserId, 50, "promo", null);

        var expired = await coins.ExpireDueBatchesAsync();

        Assert.Equal(0, expired);
        Assert.Equal(50, (await db.UserCoins.SingleAsync()).Balance);
    }
}
