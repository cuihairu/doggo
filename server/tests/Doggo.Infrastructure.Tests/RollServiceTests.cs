using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

public class RollServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid OtherUserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"roll-service-tests-{Guid.NewGuid():N}")
            .Options);

    private static RollService CreateService(DoggoDbContext db) =>
        new(db, NullLogger<RollService>.Instance);

    private static async Task<RollRoomTemplate> SeedTemplateAsync(
        DoggoDbContext db, RollRuleType rule = RollRuleType.LoginEntry, bool enabled = true)
    {
        var template = new RollRoomTemplate
        {
            Id = Guid.CreateVersion7(),
            Title = "Daily roll",
            RuleType = rule,
            DurationMinutes = 60,
            EntryDeadlineMinutes = 5,
            MaxPrizeValueCents = 100_00,
            Enabled = enabled
        };
        db.RollRoomTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task<UserOrder> SeedOrderAsync(DoggoDbContext db, Guid userId, OrderStatus status)
    {
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrderNo = $"ORD-{Guid.CreateVersion7():N}",
            Status = status
        };
        db.UserOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<RollRoomPrize> SeedPrizeAsync(
        DoggoDbContext db, Guid roomId, RollPrizeType type, long coinAmount = 0, int weight = 1, string title = "Prize")
    {
        var prize = new RollRoomPrize
        {
            Id = Guid.CreateVersion7(),
            RoomId = roomId,
            Type = type,
            CoinAmount = coinAmount,
            Weight = weight,
            Title = title
        };
        db.RollRoomPrizes.Add(prize);
        await db.SaveChangesAsync();
        return prize;
    }

    private static async Task<RollRoomParticipant> SeedParticipantAsync(
        DoggoDbContext db, Guid roomId, Guid userId, string? entryRef = null)
    {
        var participant = new RollRoomParticipant
        {
            Id = Guid.CreateVersion7(),
            RoomId = roomId,
            UserId = userId,
            EntryRef = entryRef,
            JoinedAtUtc = DateTime.UtcNow
        };
        db.RollRoomParticipants.Add(participant);
        await db.SaveChangesAsync();
        return participant;
    }

    [Fact]
    public async Task CreateRoomFromTemplateAsync_OpensRunningRoomWithDeadlines()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);

        var result = await service.CreateRoomFromTemplateAsync(template.Id);

        Assert.Equal(RollRoomStatus.Running, result.Status);
        Assert.True(result.EndAtUtc > DateTime.UtcNow);
        Assert.True(result.EntryDeadlineUtc <= result.EndAtUtc);

        var room = await db.RollRooms.SingleAsync(r => r.Id == result.RoomId);
        Assert.Equal(template.Title, room.Title);
        Assert.Equal(template.RuleType, room.RuleType);
    }

    [Fact]
    public async Task CreateRoomFromTemplateAsync_UnknownTemplate_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<RollValidationException>(() =>
            service.CreateRoomFromTemplateAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task CreateRoomFromTemplateAsync_DisabledTemplate_Throws()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db, enabled: false);
        var service = CreateService(db);

        await Assert.ThrowsAsync<RollValidationException>(() =>
            service.CreateRoomFromTemplateAsync(template.Id));
    }

    [Fact]
    public async Task JoinRoomAsync_LoginEntry_JoinsOnceAndRejectsDuplicate()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var room = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;

        var joined = await service.JoinRoomAsync(room, UserId, null);

        Assert.Equal(1, joined.EntryCount);

        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(room, UserId, null));
    }

    [Fact]
    public async Task JoinRoomAsync_PerPurchase_NeedsEligibleOwnedOrder()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db, RollRuleType.PerPurchase);
        var service = CreateService(db);
        var room = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;

        // Not a guid.
        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(room, UserId, "not-a-guid"));

        // Unpaid order does not qualify.
        var unpaid = await SeedOrderAsync(db, UserId, OrderStatus.PendingPayment);
        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(room, UserId, unpaid.Id.ToString()));

        // Order owned by someone else does not qualify.
        var foreign = await SeedOrderAsync(db, OtherUserId, OrderStatus.Completed);
        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(room, UserId, foreign.Id.ToString()));

        // A completed order qualifies, and the same order cannot enter twice.
        var paid = await SeedOrderAsync(db, UserId, OrderStatus.Completed);
        var joined = await service.JoinRoomAsync(room, UserId, paid.Id.ToString());
        Assert.Equal(1, joined.EntryCount);

        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(room, UserId, paid.Id.ToString()));
    }

    [Fact]
    public async Task JoinRoomAsync_FixedInviteCode_MatchesRoomCode()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db, RollRuleType.FixedInviteCode);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        var coded = await db.RollRooms.FirstAsync(r => r.Id == roomId);
        coded.InviteCode = "CODE-1";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(roomId, UserId, "wrong"));

        var joined = await service.JoinRoomAsync(roomId, UserId, "CODE-1");
        Assert.Equal(1, joined.EntryCount);
    }

    [Fact]
    public async Task JoinRoomAsync_AfterDeadline_Throws()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        var expired = await db.RollRooms.FirstAsync(r => r.Id == roomId);
        expired.EntryDeadlineUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<RollValidationException>(() => service.JoinRoomAsync(roomId, UserId, null));
    }

    [Fact]
    public async Task JoinRoomAsync_UnknownRoom_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<RollRoomNotFoundException>(() =>
            service.JoinRoomAsync(Guid.CreateVersion7(), UserId, null));
    }

    [Fact]
    public async Task DrawAsync_NoParticipants_ClosesWithoutWinner()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100);

        var result = await service.DrawAsync(roomId);

        Assert.Equal(RollRoomStatus.Finished, result.Status);
        Assert.Null(result.WinnerUserId);
        Assert.Null(result.WonPrizeId);
        Assert.Equal(RollRoomStatus.Finished, (await service.FindRoomAsync(roomId))?.Status);
    }

    [Fact]
    public async Task DrawAsync_NoPrizes_ClosesWithoutWinner()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);

        var result = await service.DrawAsync(roomId);

        Assert.Equal(RollRoomStatus.Finished, result.Status);
        Assert.Null(result.WinnerUserId);
        Assert.Equal(RollRoomStatus.Finished, (await service.FindRoomAsync(roomId))?.Status);
    }

    [Fact]
    public async Task DrawAsync_PicksWinnerAndWeightedPrize_IdempotentOnReplay()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        // One participant and one prize keep the random draw deterministic; the weighted
        // selection itself is covered by AliasMethodTests.
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100, weight: 1, title: "Coins");

        var result = await service.DrawAsync(roomId);

        Assert.Equal(RollRoomStatus.Drawn, result.Status);
        Assert.Equal(UserId, result.WinnerUserId);
        Assert.NotNull(result.WonPrizeId);
        Assert.Equal("Coins", result.PrizeTitle);

        var replay = await service.DrawAsync(roomId);
        Assert.Equal(result.WinnerUserId, replay.WinnerUserId);
        Assert.Equal(result.WonPrizeId, replay.WonPrizeId);
    }

    [Fact]
    public async Task DrawAsync_UnknownRoom_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<RollRoomNotFoundException>(() => service.DrawAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task DrawDueRoomsAsync_OnlyDrawsRoomsPastTheirEndTime()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var due = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        var future = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, due, UserId);
        await SeedPrizeAsync(db, due, RollPrizeType.CoinGrant, 100);
        var overdue = await db.RollRooms.FirstAsync(r => r.Id == due);
        overdue.EndAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var drawn = await service.DrawDueRoomsAsync();

        Assert.Equal(1, drawn);
        Assert.Equal(RollRoomStatus.Drawn, (await service.FindRoomAsync(due))?.Status);
        Assert.Equal(RollRoomStatus.Running, (await service.FindRoomAsync(future))?.Status);
    }

    [Fact]
    public async Task ClaimAsync_CoinPrize_GrantsBalanceAndFinishesRoom()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 250);
        await service.DrawAsync(roomId);

        var claim = await service.ClaimAsync(roomId, UserId);

        Assert.Equal(RollPrizeType.CoinGrant, claim.PrizeType);
        Assert.Equal(250, claim.CoinAmount);
        Assert.Equal(RollRoomStatus.Finished, (await service.FindRoomAsync(roomId))?.Status);

        var balance = await db.UserCoins.Where(c => c.UserId == UserId).Select(c => c.Balance).SingleAsync();
        Assert.Equal(250, balance);
        var log = await db.UserCoinLogs.SingleAsync(l => l.UserId == UserId);
        Assert.Equal(250, log.Delta);
        Assert.Equal(250, log.BalanceAfter);

        // Replay returns the same result without granting twice.
        var replay = await service.ClaimAsync(roomId, UserId);
        Assert.Equal(claim.PrizeId, replay.PrizeId);
        Assert.Equal(250, (await db.UserCoins.Where(c => c.UserId == UserId).Select(c => c.Balance).SingleAsync()));
    }

    [Fact]
    public async Task ClaimAsync_InventoryPrize_IssuesUserPrizeFromTheRoom()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.InventoryItem, title: "AK-47 | Redline");
        await service.DrawAsync(roomId);

        var claim = await service.ClaimAsync(roomId, UserId);

        Assert.Equal(RollPrizeType.InventoryItem, claim.PrizeType);
        Assert.NotNull(claim.PrizeId);

        var prize = await db.UserPrizes.SingleAsync(p => p.Id == claim.PrizeId);
        Assert.Equal(UserId, prize.UserId);
        Assert.Equal(roomId, prize.OrderId);
        Assert.Equal(PrizeSource.Roll, prize.Source);
        Assert.Equal("AK-47 | Redline", prize.MarketHashName);
    }

    [Fact]
    public async Task ClaimAsync_NonWinner_Throws()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedParticipantAsync(db, roomId, OtherUserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100);
        await service.DrawAsync(roomId);

        // The draw picks the winner uniformly; the loser must not be able to claim.
        var winnerId = await db.RollRoomParticipants
            .Where(p => p.RoomId == roomId && p.WonPrizeId != null)
            .Select(p => p.UserId)
            .SingleAsync();
        var loserId = winnerId == UserId ? OtherUserId : UserId;

        await Assert.ThrowsAsync<RollValidationException>(() => service.ClaimAsync(roomId, loserId));

        var claim = await service.ClaimAsync(roomId, winnerId);
        Assert.Equal(100, claim.CoinAmount);
    }

    [Fact]
    public async Task OpenNextPeriodsAsync_RollsFinishedRoomsFromEnabledTemplates()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100);
        await service.DrawAsync(roomId);
        await service.ClaimAsync(roomId, UserId);

        var rolled = await service.OpenNextPeriodsAsync();

        Assert.Equal(1, rolled);
        var next = await db.RollRooms.SingleAsync(r => r.TemplateId == template.Id && r.Id != roomId);
        Assert.Equal(RollRoomStatus.Running, next.Status);
    }

    [Fact]
    public async Task OpenNextPeriodsAsync_DoesNotRollWhileALiveRoomExists()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100);
        await service.DrawAsync(roomId);
        await service.ClaimAsync(roomId, UserId);

        // A second (still running) room from the same template means the period is still live.
        await service.CreateRoomFromTemplateAsync(template.Id);

        var rolled = await service.OpenNextPeriodsAsync();

        Assert.Equal(0, rolled);
        Assert.Equal(2, await db.RollRooms.CountAsync(r => r.TemplateId == template.Id));
    }

    [Fact]
    public async Task OpenNextPeriodsAsync_SkipsDisabledTemplates()
    {
        await using var db = CreateDb();
        var template = await SeedTemplateAsync(db);
        var service = CreateService(db);
        var roomId = (await service.CreateRoomFromTemplateAsync(template.Id)).RoomId;
        await SeedParticipantAsync(db, roomId, UserId);
        await SeedPrizeAsync(db, roomId, RollPrizeType.CoinGrant, 100);
        await service.DrawAsync(roomId);
        await service.ClaimAsync(roomId, UserId);
        var disabled = await db.RollRoomTemplates.FirstAsync(t => t.Id == template.Id);
        disabled.Enabled = false;
        await db.SaveChangesAsync();

        var rolled = await service.OpenNextPeriodsAsync();

        Assert.Equal(0, rolled);
        Assert.Equal(1, await db.RollRooms.CountAsync(r => r.TemplateId == template.Id));
    }
}
