using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class InviteServiceTests
{
    private static InviteService CreateService(DoggoDbContext db, long reward = 500)
    {
        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        return new InviteService(db, ledger, Options.Create(new InviteOptions { CoinReward = reward }), NullLogger<InviteService>.Instance);
    }

    private static async Task<(Guid InviterId, Guid InviteeId)> SeedUsersAsync(DoggoDbContext db)
    {
        var inviter = Guid.CreateVersion7();
        var invitee = Guid.CreateVersion7();
        db.UserAccounts.AddRange(
            new UserAccount
            {
                Id = inviter,
                Phone = "13900000001",
                Email = string.Empty,
                PasswordHash = "x",
                Nickname = "inviter",
                InviteCode = "INVITER1"
            },
            new UserAccount
            {
                Id = invitee,
                Phone = "13900000002",
                Email = string.Empty,
                PasswordHash = "x",
                Nickname = "invitee",
                InviteCode = "INVITEE1",
                InvitedByUserId = inviter
            });
        await db.SaveChangesAsync();
        return (inviter, invitee);
    }

    [Fact]
    public async Task First_completed_order_pays_the_inviter_once()
    {
        await using var db = new DoggoDbContext(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"invite-tests-{Guid.NewGuid():N}").Options);
        var (inviterId, inviteeId) = await SeedUsersAsync(db);
        var invites = CreateService(db);
        var orderId = Guid.CreateVersion7();

        Assert.True(await invites.GrantRewardAsync(inviteeId, orderId));
        // Callback replays and repeats are refused.
        Assert.False(await invites.GrantRewardAsync(inviteeId, orderId));

        Assert.Equal(500, (await db.UserCoins.SingleAsync(c => c.UserId == inviterId)).Balance);
        var log = await db.InviteRewardLogs.SingleAsync();
        Assert.Equal(inviterId, log.InviterUserId);
        Assert.Equal(inviteeId, log.InviteeUserId);
    }

    [Fact]
    public async Task A_user_without_inviter_is_a_no_op()
    {
        await using var db = new DoggoDbContext(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"invite-tests-{Guid.NewGuid():N}").Options);
        var lone = Guid.CreateVersion7();
        db.UserAccounts.Add(new UserAccount
        {
            Id = lone,
            Phone = "13900000003",
            Email = string.Empty,
            PasswordHash = "x",
            Nickname = "lone",
            InviteCode = "LONE0001"
        });
        await db.SaveChangesAsync();

        Assert.False(await CreateService(db).GrantRewardAsync(lone, Guid.CreateVersion7()));
        Assert.Empty(db.InviteRewardLogs);
    }
}

public class SignServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"sign-tests-{Guid.NewGuid():N}").Options);

    private static (SignService Signs, CoinService Ledger) CreateServices(DoggoDbContext db, long reward = 50)
    {
        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        return (new SignService(db, ledger, Options.Create(new SignOptions { CoinReward = reward }), NullLogger<SignService>.Instance), ledger);
    }

    private static async Task<UserOrder> SeedCompletedOrderAsync(DoggoDbContext db, Guid userId, string orderNo)
    {
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrderNo = orderNo,
            ProductId = Guid.CreateVersion7(),
            RegularPriceCents = 1000,
            PriceCents = 1000,
            TotalPriceCents = 1000,
            TotalCostCents = 1000,
            Entry = OrderEntry.Case,
            Status = OrderStatus.Completed,
            ServiceMode = ServiceMode.CloudOpen,
            FulfillmentType = FulfillmentType.Spot,
            PayType = PaymentProvider.Alipay,
            CompletedAtUtc = DateTime.UtcNow
        };
        db.UserOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Signing_today_credits_reward_and_sets_the_weekly_cycle()
    {
        await using var db = CreateDb();
        var (signs, _) = CreateServices(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expectedCycle = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var result = await signs.SignTodayAsync(UserId);

        Assert.Equal(expectedCycle, result.CycleId);
        Assert.Equal(today, result.SignedDate);
        Assert.Equal(50, result.CoinReward);
        Assert.Equal(50, result.CycleTotalReward);
        var record = await db.SignRecords.SingleAsync();
        Assert.Null(record.MakeupOrderNo);
        Assert.Equal(50, (await db.UserCoins.SingleAsync()).Balance);
    }

    [Fact]
    public async Task Signing_twice_on_the_same_day_is_rejected()
    {
        await using var db = CreateDb();
        var (signs, _) = CreateServices(db);
        await signs.SignTodayAsync(UserId);

        await Assert.ThrowsAsync<SignValidationException>(() => signs.SignTodayAsync(UserId));
    }

    [Fact]
    public async Task Makeup_needs_a_completed_order_and_each_order_works_once()
    {
        await using var db = CreateDb();
        var (signs, _) = CreateServices(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cycleStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        // On Monday there is no earlier day inside the cycle; the valid-makeup branch below
        // is skipped and only the rejection paths run.
        var missed = cycleStart != today ? cycleStart : today;
        var order = await SeedCompletedOrderAsync(db, UserId, "DO-MAKEUP-1");

        // No order number given.
        await Assert.ThrowsAsync<SignValidationException>(() => signs.SignAsync(UserId, missed, null));

        // Someone else's order is refused.
        var strangerOrder = await SeedCompletedOrderAsync(db, Guid.CreateVersion7(), "DO-MAKEUP-2");
        await Assert.ThrowsAsync<SignValidationException>(() => signs.SignAsync(UserId, missed, "DO-MAKEUP-2"));

        // A pending order is refused.
        var pending = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            OrderNo = "DO-MAKEUP-3",
            ProductId = Guid.CreateVersion7(),
            Entry = OrderEntry.Case,
            Status = OrderStatus.PendingPayment,
            ServiceMode = ServiceMode.CloudOpen,
            FulfillmentType = FulfillmentType.Spot,
            PayType = PaymentProvider.Alipay
        };
        db.UserOrders.Add(pending);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SignValidationException>(() => signs.SignAsync(UserId, missed, "DO-MAKEUP-3"));

        var secondMissed = missed.AddDays(1);
        var hasTwoMissedDays = secondMissed < today;

        // The valid makeup works once; the same order cannot cover a second day.
        if (missed != today)
        {
            var result = await signs.SignAsync(UserId, missed, "DO-MAKEUP-1");
            Assert.Equal(50, result.CoinReward);
            var record = await db.SignRecords.SingleAsync(s => s.SignedDate == missed);
            Assert.Equal("DO-MAKEUP-1", record.MakeupOrderNo);
            if (hasTwoMissedDays)
            {
                await Assert.ThrowsAsync<SignValidationException>(() => signs.SignAsync(UserId, secondMissed, "DO-MAKEUP-1"));
            }
        }

        // Future days are refused outright.
        await Assert.ThrowsAsync<SignValidationException>(() => signs.SignAsync(UserId, today.AddDays(1), "DO-MAKEUP-1"));
    }

    [Fact]
    public async Task Cycle_view_lists_days_and_flags_today()
    {
        await using var db = CreateDb();
        var (signs, _) = CreateServices(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await signs.SignTodayAsync(UserId);

        var view = await signs.GetCycleAsync(UserId);

        Assert.Equal(today, view.Days.Max(d => d.Date));
        Assert.True(view.TodaySigned);
        Assert.Contains(view.Days, d => d.Signed);
    }
}
