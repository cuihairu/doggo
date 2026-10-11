using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Cases;

public sealed class RollRoomNotFoundException(string message) : Exception(message)
{
}

public sealed class RollValidationException(string message) : Exception(message)
{
}

public sealed record CreateRoomResult(Guid RoomId, RollRoomStatus Status, DateTime EndAtUtc, DateTime? EntryDeadlineUtc);

public sealed record JoinRoomResult(Guid RoomId, Guid UserId, int EntryCount);

public sealed record DrawResult(Guid RoomId, RollRoomStatus Status, Guid? WinnerUserId, Guid? WonPrizeId, string? PrizeTitle);

public sealed record ClaimResult(Guid RoomId, Guid UserId, RollPrizeType PrizeType, long CoinAmount, Guid? PrizeId);

public sealed record RollRoomView(
    Guid Id,
    Guid? TemplateId,
    string Title,
    RollRuleType RuleType,
    RollRoomStatus Status,
    string? InviteCode,
    string? CoverUrl,
    long MaxPrizeValueCents,
    DateTime? StartAtUtc,
    DateTime? EndAtUtc,
    DateTime? EntryDeadlineUtc,
    int ParticipantCount,
    IReadOnlyList<RollPrizeView> Prizes);

public sealed record RollPrizeView(Guid Id, RollPrizeType Type, string Title, string ImageUrl, int Weight, long CoinAmount);

/// <summary>
/// Roll room flow: rooms open from a template, users join under the room's rule, a worker
/// sweep draws rooms past their end time with AliasMethod-weighted prize selection, the
/// winner claims (inventory item prize or coin grant), and the sweep rolls the next period
/// from the same template. Prize weights decide which prize is won; the winner participant
/// is drawn uniformly.
/// </summary>
public sealed partial class RollService(DoggoDbContext db, ILogger<RollService> logger)
{
    public async Task<CreateRoomResult> CreateRoomFromTemplateAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var template = await db.RollRoomTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken)
            ?? throw new RollValidationException($"Roll template {templateId} was not found.");

        if (!template.Enabled)
        {
            throw new RollValidationException($"Roll template {template.Title} is disabled.");
        }

        var now = DateTime.UtcNow;
        var room = new RollRoom
        {
            TemplateId = template.Id,
            Title = template.Title,
            RuleType = template.RuleType,
            Status = RollRoomStatus.Running,
            MaxPrizeValueCents = template.MaxPrizeValueCents,
            StartAtUtc = now,
            EndAtUtc = now.AddMinutes(Math.Max(template.DurationMinutes, 1)),
            EntryDeadlineUtc = now.AddMinutes(Math.Max(template.EntryDeadlineMinutes, 0))
        };
        db.RollRooms.Add(room);
        await db.SaveChangesAsync(cancellationToken);
        LogRoomCreated(room.Title, room.EndAtUtc.Value);
        return new CreateRoomResult(room.Id, room.Status, room.EndAtUtc.Value, room.EntryDeadlineUtc.Value);
    }

    public async Task<JoinRoomResult> JoinRoomAsync(Guid roomId, Guid userId, string? entryRef, CancellationToken cancellationToken = default)
    {
        var room = await db.RollRooms
            .FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken)
            ?? throw new RollRoomNotFoundException($"Roll room {roomId} was not found.");

        if (room.Status != RollRoomStatus.Running)
        {
            throw new RollValidationException($"Roll room {roomId} is not running.");
        }

        if (room.EntryDeadlineUtc is { } deadline && DateTime.UtcNow > deadline)
        {
            throw new RollValidationException("Entry deadline has passed.");
        }

        entryRef = string.IsNullOrWhiteSpace(entryRef) ? null : entryRef.Trim();
        await ValidateRuleAsync(room, entryRef, userId, cancellationToken);

        db.RollRoomParticipants.Add(new RollRoomParticipant
        {
            RoomId = room.Id,
            UserId = userId,
            EntryRef = entryRef,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);

        var count = await db.RollRoomParticipants.AsNoTracking()
            .CountAsync(p => p.RoomId == room.Id, cancellationToken);
        LogJoined(room.Title, userId, count);
        return new JoinRoomResult(room.Id, userId, count);
    }

    /// <summary>
    /// Draws a due room: uniformly picks the winner participant and AliasMethod-samples the
    /// prize by weight. Idempotent on room status — an already-drawn room returns its winner.
    /// </summary>
    public async Task<DrawResult> DrawAsync(Guid roomId, CancellationToken cancellationToken = default)
    {
        var room = await db.RollRooms
            .FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken)
            ?? throw new RollRoomNotFoundException($"Roll room {roomId} was not found.");

        if (room.Status is RollRoomStatus.Drawn or RollRoomStatus.Claiming or RollRoomStatus.Finished)
        {
            var existing = await LoadWinnerAsync(room.Id, cancellationToken);
            return new DrawResult(room.Id, room.Status, existing?.UserId, existing?.WonPrizeId, null);
        }

        if (room.Status != RollRoomStatus.Running)
        {
            throw new RollValidationException($"Roll room {roomId} cannot be drawn from status {room.Status}.");
        }

        var participants = await db.RollRoomParticipants
            .Where(p => p.RoomId == room.Id)
            .OrderBy(p => p.JoinedAtUtc)
            .ToListAsync(cancellationToken);
        var prizes = await db.RollRoomPrizes.AsNoTracking()
            .Where(p => p.RoomId == room.Id)
            .ToListAsync(cancellationToken);

        RollRoomStateMachine.RequireTransition(room.Status, RollRoomStatus.Drawing);
        room.Status = RollRoomStatus.Drawing;

        if (participants.Count == 0 || prizes.Count == 0)
        {
            // Nothing to draw and nothing to claim: close the room so the sweep can roll
            // the template's next period.
            RollRoomStateMachine.RequireTransition(RollRoomStatus.Drawing, RollRoomStatus.Drawn);
            room.Status = RollRoomStatus.Drawn;
            RollRoomStateMachine.RequireTransition(RollRoomStatus.Drawn, RollRoomStatus.Finished);
            room.Status = RollRoomStatus.Finished;
            await db.SaveChangesAsync(cancellationToken);
            LogDrawnNoWinner(room.Title);
            return new DrawResult(room.Id, room.Status, null, null, null);
        }

        var winner = participants[Random.Shared.Next(participants.Count)];
        var prize = prizes.Count == 1
            ? prizes[0]
            : prizes[NewSampler(prizes).Sample(Random.Shared)];

        RollRoomStateMachine.RequireTransition(RollRoomStatus.Drawing, RollRoomStatus.Drawn);
        room.Status = RollRoomStatus.Drawn;
        winner.WonPrizeId = prize.Id;
        await db.SaveChangesAsync(cancellationToken);
        LogDrawn(room.Title, winner.UserId, prize.Title);
        return new DrawResult(room.Id, room.Status, winner.UserId, prize.Id, prize.Title);
    }

    /// <summary>Sweep entry: draws every running room past its end time.</summary>
    public async Task<int> DrawDueRoomsAsync(CancellationToken cancellationToken = default)
    {
        var due = await db.RollRooms.AsNoTracking()
            .Where(r => r.Status == RollRoomStatus.Running && r.EndAtUtc <= DateTime.UtcNow)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var roomId in due)
        {
            try
            {
                await DrawAsync(roomId, cancellationToken);
            }
            catch (Exception ex)
            {
                LogDrawFailed(roomId, ex.Message);
            }
        }

        if (due.Count > 0)
        {
            LogSwept(due.Count);
        }

        return due.Count;
    }

    /// <summary>Winner claims: inventory prizes become UserPrize records, coin prizes credit the balance.</summary>
    public async Task<ClaimResult> ClaimAsync(Guid roomId, Guid userId, CancellationToken cancellationToken = default)
    {
        var room = await db.RollRooms
            .FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken)
            ?? throw new RollRoomNotFoundException($"Roll room {roomId} was not found.");

        var participant = await db.RollRoomParticipants
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId, cancellationToken)
            ?? throw new RollValidationException("Only the winner can claim.");

        if (participant.WonPrizeId is null)
        {
            throw new RollValidationException("This participant did not win.");
        }

        var prize = await db.RollRoomPrizes
            .FirstOrDefaultAsync(p => p.Id == participant.WonPrizeId, cancellationToken)
            ?? throw new RollValidationException("The won prize no longer exists.");

        if (participant.ClaimedAtUtc is not null)
        {
            return new ClaimResult(room.Id, userId, prize.Type, prize.CoinAmount, prize.UserPrizeId);
        }

        RollRoomStateMachine.RequireTransition(room.Status, RollRoomStatus.Claiming);
        room.Status = RollRoomStatus.Claiming;

        Guid? userPrizeId = null;
        if (prize.Type == RollPrizeType.InventoryItem)
        {
            // The prize inventory item is delivered through the retrieval flow; here the
            // prize record is issued so the user sees it in "my prizes" right away.
            var userPrize = new UserPrize
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                OrderId = room.Id, // no purchase order behind a roll win; the room is the source
                Source = PrizeSource.Roll,
                AssetId = Guid.CreateVersion7().ToString("N"),
                ClassId = string.Empty,
                InstanceId = string.Empty,
                MarketHashName = prize.Title,
                Quality = CaseQuality.RareSpecial,
                Rarity = "roll",
                IconUrl = prize.ImageUrl,
                PriceCents = 0
            };
            db.UserPrizes.Add(userPrize);
            userPrizeId = userPrize.Id;
            prize.UserPrizeId = userPrizeId; // replayed claims hand back the same record
        }
        else
        {
            await GrantCoinsAsync(userId, prize.CoinAmount, room.Id, cancellationToken);
        }

        participant.ClaimedAtUtc = DateTime.UtcNow;
        RollRoomStateMachine.RequireTransition(RollRoomStatus.Claiming, RollRoomStatus.Finished);
        room.Status = RollRoomStatus.Finished;
        await db.SaveChangesAsync(cancellationToken);
        LogClaimed(room.Title, userId, prize.Type, prize.CoinAmount);
        return new ClaimResult(room.Id, userId, prize.Type, prize.CoinAmount, userPrizeId);
    }

    /// <summary>
    /// Rolls the next period: a template with finished rooms but no live room gets one fresh
    /// room. Called by the worker sweep; rooms keep their history, periods are new ids. The
    /// "no live room" check is what keeps the sweep from spawning a new period every tick.
    /// </summary>
    public async Task<int> OpenNextPeriodsAsync(CancellationToken cancellationToken = default)
    {
        var finishedTemplateIds = await db.RollRooms.AsNoTracking()
            .Where(r => r.Status == RollRoomStatus.Finished && r.TemplateId != null)
            .Select(r => r.TemplateId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (finishedTemplateIds.Count == 0)
        {
            return 0;
        }

        var enabledTemplateIds = await db.RollRoomTemplates.AsNoTracking()
            .Where(t => finishedTemplateIds.Contains(t.Id) && t.Enabled)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var liveTemplateIds = await db.RollRooms.AsNoTracking()
            .Where(r => r.TemplateId != null && r.Status != RollRoomStatus.Finished)
            .Select(r => r.TemplateId!.Value)
            .ToListAsync(cancellationToken);

        var rolled = 0;
        foreach (var templateId in enabledTemplateIds.Except(liveTemplateIds))
        {
            await CreateRoomFromTemplateAsync(templateId, cancellationToken);
            rolled++;
        }

        if (rolled > 0)
        {
            LogRolled(rolled);
        }

        return rolled;
    }

    public async Task<IReadOnlyList<RollRoom>> ListRunningRoomsAsync(CancellationToken cancellationToken = default) =>
        await db.RollRooms.AsNoTracking()
            .Where(r => r.Status == RollRoomStatus.Running)
            .OrderBy(r => r.EndAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<RollRoom?> FindRoomAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        await db.RollRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken);

    public async Task<IReadOnlyList<RollRoomPrize>> ListPrizesAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        await db.RollRoomPrizes.AsNoTracking()
            .Where(p => p.RoomId == roomId)
            .OrderBy(p => p.Type)
            .ThenByDescending(p => p.Weight)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RollRoomParticipant>> ListParticipantsAsync(Guid roomId, CancellationToken cancellationToken = default) =>
        await db.RollRoomParticipants.AsNoTracking()
            .Where(p => p.RoomId == roomId)
            .OrderBy(p => p.JoinedAtUtc)
            .ToListAsync(cancellationToken);

    private async Task ValidateRuleAsync(RollRoom room, string? entryRef, Guid userId, CancellationToken cancellationToken)
    {
        var dedupPerUser = false;
        var dedupPerEntry = false;

        switch (room.RuleType)
        {
            case RollRuleType.LoginEntry:
                // Logged-in users join once; entryRef is ignored.
                dedupPerUser = true;
                break;

            case RollRuleType.PerPurchase:
            case RollRuleType.FreeBundle:
            case RollRuleType.AllBlueFree:
            case RollRuleType.RedDropFree:
            case RollRuleType.GoldDropFree:
                if (!Guid.TryParse(entryRef, out var orderId))
                {
                    throw new RollValidationException("This room needs an order id to join.");
                }

                var order = await db.UserOrders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken);
                if (order is null || order.Status is OrderStatus.PendingPayment or OrderStatus.Cancelled or OrderStatus.Refunded)
                {
                    throw new RollValidationException("No eligible order found for this user.");
                }

                // One entry per qualifying order.
                dedupPerEntry = true;
                break;

            case RollRuleType.FixedInviteCode:
                if (string.IsNullOrEmpty(room.InviteCode) || !string.Equals(entryRef, room.InviteCode, StringComparison.Ordinal))
                {
                    throw new RollValidationException("Invite code does not match.");
                }

                dedupPerUser = true;
                break;

            case RollRuleType.RandomInviteCode:
                // Codes are issued per user outside the room; joining needs any non-empty code.
                if (string.IsNullOrEmpty(entryRef))
                {
                    throw new RollValidationException("An invite code is required.");
                }

                dedupPerEntry = true;
                break;

            default:
                throw new RollValidationException($"Rule {room.RuleType} is not supported.");
        }

        var duplicate = dedupPerUser
            ? await db.RollRoomParticipants.AsNoTracking()
                .AnyAsync(p => p.RoomId == room.Id && p.UserId == userId, cancellationToken)
            : dedupPerEntry && entryRef is not null && await db.RollRoomParticipants.AsNoTracking()
                .AnyAsync(p => p.RoomId == room.Id && p.UserId == userId && p.EntryRef == entryRef, cancellationToken);
        if (duplicate)
        {
            throw new RollValidationException("This user already joined the room.");
        }
    }

    private static AliasSampler NewSampler(IReadOnlyList<RollRoomPrize> prizes) =>
        new(prizes.Select(p => p.Weight).ToList());

    private async Task<RollRoomParticipant?> LoadWinnerAsync(Guid roomId, CancellationToken cancellationToken) =>
        await db.RollRoomParticipants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.RoomId == roomId && p.WonPrizeId != null, cancellationToken);

    private async Task GrantCoinsAsync(Guid userId, long amount, Guid roomId, CancellationToken cancellationToken)
    {
        var account = await db.UserCoins.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (account is null)
        {
            account = new UserCoin { UserId = userId, Balance = 0 };
            db.UserCoins.Add(account);
        }

        account.Balance += amount;
        db.UserCoinBatches.Add(new UserCoinBatch
        {
            UserId = userId,
            OriginalAmount = amount,
            RemainingAmount = amount,
            SourceType = "roll_room",
            SourceRef = roomId.ToString()
        });
        db.UserCoinLogs.Add(new UserCoinLog
        {
            UserId = userId,
            Type = CoinLogType.Grant,
            Delta = amount,
            BalanceAfter = account.Balance,
            RefType = "roll_room",
            RefId = roomId.ToString()
        });
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "roll room {Title} opened, ends at {EndAtUtc}")]
    private partial void LogRoomCreated(string title, DateTime endAtUtc);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} joined roll room {Title} ({EntryCount} entries)")]
    private partial void LogJoined(string title, Guid userId, int entryCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "roll room {Title} drawn for user {UserId}: {PrizeTitle}")]
    private partial void LogDrawn(string title, Guid userId, string prizeTitle);

    [LoggerMessage(Level = LogLevel.Information, Message = "roll room {Title} drawn with no eligible winner")]
    private partial void LogDrawnNoWinner(string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "roll room draw failed for {RoomId}: {Reason}")]
    private partial void LogDrawFailed(Guid roomId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} roll rooms drawn by sweep")]
    private partial void LogSwept(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} claimed roll room {Title} ({Type}, {CoinAmount} coins)")]
    private partial void LogClaimed(string title, Guid userId, RollPrizeType type, long coinAmount);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} roll rooms rolled into the next period")]
    private partial void LogRolled(int count);
}
