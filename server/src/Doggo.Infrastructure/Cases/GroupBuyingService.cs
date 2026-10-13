using System.Text.Json.Serialization;
using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Cases;

public sealed class BatchNotFoundException(string message) : Exception(message)
{
}

public sealed class SeatNotFoundException(string message) : Exception(message)
{
}

public sealed class BatchSoldOutException(string message) : Exception(message)
{
}

public sealed class CaseValidationException(string message) : Exception(message)
{
}

public sealed class CaseOpeningUnavailableException(string message) : Exception(message)
{
}

public sealed record BatchSeatInput(string CrateAssetId, string? KeyAssetId);

public sealed record CreateBatchCommand(Guid ProductId, string Lot, string VaporCaseId, IReadOnlyList<BatchSeatInput> Seats);

public sealed record CreateBatchResult(Guid BatchId, string Lot, int SeatCount);

public sealed record ClaimSeatCommand(Guid BatchId, Guid OrderId);

public sealed record ClaimSeatResult(Guid BatchId, int SeatNo, int SoldCount, int SeatCount);

public sealed record SubmitOpeningCommand(Guid BatchId, int SeatNo);

public sealed record OpeningResultItem(
    string AssetId,
    string ClassId,
    string InstanceId,
    string MarketHashName,
    CaseQuality Quality,
    string Exterior,
    double? FloatValue,
    string Rarity,
    string IconUrl,
    string? InspectLink,
    long PriceCents,
    DateTime? TradableAtUtc);

public sealed record ApplyOpeningResultCommand(
    Guid BatchId,
    int SeatNo,
    OpeningResultItem Item,
    long? HistoryId,
    string? RawJson);

public sealed record OpeningResultApplied(bool Applied, Guid? PrizeId);

/// <summary>
/// Batch/seat lifecycle for platform-side case opening. Seats are sold from a batch with an
/// optimistic-lock guard on the batch row (Version is a concurrency token, so two workers
/// claiming the last seat cannot both win), the opening dispatch goes through the vapor
/// case_open action, and the drop result arrives as an event that creates the UserPrize
/// and advances the seat. Seat moves are linear through <see cref="SeatStateMachine"/>.
/// </summary>
public sealed partial class GroupBuyingService(
    DoggoDbContext db,
    VaporClient vapor,
    ILogger<GroupBuyingService> logger)
{
    public async Task<CreateBatchResult> CreateBatchAsync(CreateBatchCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Lot))
        {
            throw new CaseValidationException("Batch lot is required.");
        }

        if (string.IsNullOrWhiteSpace(command.VaporCaseId))
        {
            throw new CaseValidationException("Batch vapor case id is required.");
        }

        if (command.Seats.Count == 0)
        {
            throw new CaseValidationException("Batch needs at least one seat.");
        }

        if (command.Seats.Any(seat => string.IsNullOrWhiteSpace(seat.CrateAssetId)))
        {
            throw new CaseValidationException("Every seat needs a crate asset id.");
        }

        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            ?? throw new CaseValidationException($"Product {command.ProductId} was not found.");

        if (product.Type != ProductType.Crate)
        {
            throw new CaseValidationException("Batches can only be created for crates.");
        }

        var lot = command.Lot.Trim();
        var duplicate = await db.GroupBuyings.AsNoTracking()
            .AnyAsync(b => b.ProductId == command.ProductId && b.Lot == lot, cancellationToken);
        if (duplicate)
        {
            throw new CaseValidationException($"Batch lot {lot} already exists for this product.");
        }

        var batch = new GroupBuying
        {
            Id = Guid.CreateVersion7(),
            ProductId = command.ProductId,
            Lot = lot,
            VaporCaseId = command.VaporCaseId.Trim(),
            SeatCount = command.Seats.Count,
            Version = 1
        };
        db.GroupBuyings.Add(batch);

        for (var i = 0; i < command.Seats.Count; i++)
        {
            db.GroupBuyingSeats.Add(new GroupBuyingSeat
            {
                // Explicit ids: the audit pipeline fills empty ones at save time, but adding
                // several seats in one context needs distinct keys at track time already.
                Id = Guid.CreateVersion7(),
                GroupBuyingId = batch.Id,
                SeatNo = i + 1,
                CrateAssetId = command.Seats[i].CrateAssetId,
                KeyAssetId = command.Seats[i].KeyAssetId
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        LogBatchCreated(command.ProductId, lot, command.Seats.Count);
        return new CreateBatchResult(batch.Id, lot, command.Seats.Count);
    }

    /// <summary>
    /// Sells the lowest on-sale seat of the batch to an order. The Version bump is the
    /// optimistic-lock guard: when two claims race on the final seat, the loser's
    /// SaveChanges throws on the stale Version and surfaces as sold out.
    /// </summary>
    public async Task<ClaimSeatResult> ClaimSeatAsync(ClaimSeatCommand command, CancellationToken cancellationToken = default)
    {
        var batch = await db.GroupBuyings
            .FirstOrDefaultAsync(b => b.Id == command.BatchId, cancellationToken)
            ?? throw new BatchNotFoundException($"Batch {command.BatchId} was not found.");

        if (batch.Status != GroupBuyingStatus.OnSale)
        {
            throw new BatchSoldOutException($"Batch {command.BatchId} has no seats left.");
        }

        var seat = await db.GroupBuyingSeats
            .Where(s => s.GroupBuyingId == command.BatchId && s.Status == SeatStatus.OnSale)
            .OrderBy(s => s.SeatNo)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BatchSoldOutException($"Batch {command.BatchId} has no seats left.");

        SeatStateMachine.RequireTransition(seat.Status, SeatStatus.Locked);
        seat.Status = SeatStatus.Locked;
        seat.OrderId = command.OrderId;

        batch.SoldCount++;
        batch.Version++;
        if (batch.SoldCount >= batch.SeatCount)
        {
            batch.Status = GroupBuyingStatus.SoldOut;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker sold from the same batch snapshot; to this claim the seat is gone.
            throw new BatchSoldOutException($"Batch {command.BatchId} has no seats left.");
        }

        LogSeatClaimed(batch.Lot, seat.SeatNo, command.OrderId);
        return new ClaimSeatResult(batch.Id, seat.SeatNo, batch.SoldCount, batch.SeatCount);
    }

    /// <summary>Puts a locked seat back on sale (order cancelled before payment).</summary>
    public async Task<int> ReleaseSeatAsync(Guid batchId, int seatNo, CancellationToken cancellationToken = default)
    {
        var (batch, seat) = await LoadSeatAsync(batchId, seatNo, cancellationToken);

        SeatStateMachine.RequireTransition(seat.Status, SeatStatus.OnSale);
        seat.Status = SeatStatus.OnSale;
        seat.OrderId = null;

        batch.SoldCount = Math.Max(batch.SoldCount - 1, 0);
        batch.Version++;
        if (batch.Status == GroupBuyingStatus.SoldOut && batch.SoldCount < batch.SeatCount)
        {
            batch.Status = GroupBuyingStatus.OnSale;
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeatReleased(batch.Lot, seatNo);
        return seatNo;
    }

    /// <summary>Confirms payment for a locked seat; normally driven by the payment callback.</summary>
    public async Task<int> MarkSeatPaidAsync(Guid batchId, int seatNo, CancellationToken cancellationToken = default)
    {
        var (batch, seat) = await LoadSeatAsync(batchId, seatNo, cancellationToken);

        SeatStateMachine.RequireTransition(seat.Status, SeatStatus.Paid);
        seat.Status = SeatStatus.Paid;
        await db.SaveChangesAsync(cancellationToken);
        LogSeatPaid(batch.Lot, seatNo);
        return seatNo;
    }

    /// <summary>
    /// Submits the seat's crate to vapor's case_open action. The seat is moved to Opening
    /// first; if the dispatch fails it returns to Paid for retry. The drop itself arrives
    /// later as an event handled by <see cref="ApplyOpeningResultAsync"/>.
    /// </summary>
    public async Task<Guid> SubmitOpeningAsync(SubmitOpeningCommand command, CancellationToken cancellationToken = default)
    {
        if (!vapor.IsConfigured)
        {
            throw new CaseOpeningUnavailableException("vapor is not configured; opening is unavailable.");
        }

        var (batch, seat) = await LoadSeatAsync(command.BatchId, command.SeatNo, cancellationToken);

        // Real opens burn one crate + one key, so vapor needs both asset ids together
        // with the case's catalog id; validate before the seat leaves Paid.
        if (string.IsNullOrWhiteSpace(seat.CrateAssetId) || string.IsNullOrWhiteSpace(seat.KeyAssetId))
        {
            throw new CaseValidationException($"Seat {seat.SeatNo} needs crate and key asset ids before opening.");
        }

        if (string.IsNullOrWhiteSpace(batch.VaporCaseId))
        {
            throw new CaseValidationException($"Batch {batch.Lot} has no vapor case id.");
        }

        SeatStateMachine.RequireTransition(seat.Status, SeatStatus.Opening);
        seat.Status = SeatStatus.Opening;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var job = await vapor.DispatchAndWaitAsync(
                "case_open",
                new CaseOpenDispatchPayload(batch.VaporCaseId, seat.CrateAssetId, seat.KeyAssetId),
                cancellationToken: cancellationToken);

            var failed = (job.Tasks ?? [])
                .FirstOrDefault(t => !VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Finished));
            if (failed is not null)
            {
                throw new VaporClientException($"vapor job {job.Id} task failed: {failed.Error ?? failed.Status}");
            }

            LogOpeningSubmitted(batch.Lot, seat.SeatNo, job.Id);
            return job.Id;
        }
        catch (VaporClientException)
        {
            seat.Status = SeatStatus.Paid;
            await db.SaveChangesAsync(cancellationToken);
            LogOpeningDispatchFailed(batch.Lot, seat.SeatNo);
            throw;
        }
    }

    /// <summary>
    /// Applies one opening result event: marks the seat Opened, records the prize with its
    /// full item snapshot and writes the prize log. Idempotent per (order, asset) — a replayed
    /// event returns the existing prize instead of creating a duplicate.
    /// </summary>
    public async Task<OpeningResultApplied> ApplyOpeningResultAsync(
        ApplyOpeningResultCommand command,
        CancellationToken cancellationToken = default)
    {
        var (batch, seat) = await LoadSeatAsync(command.BatchId, command.SeatNo, cancellationToken);

        var order = seat.OrderId is { } orderId
            ? await db.UserOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            : null;
        if (order is null)
        {
            throw new CaseValidationException($"Seat {command.SeatNo} of batch {command.BatchId} has no bound order.");
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PendingPayment)
        {
            throw new CaseValidationException($"Order {order.OrderNo} is not eligible for a prize (status {order.Status}).");
        }

        var existing = await db.UserPrizes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrderId == order.Id && p.AssetId == command.Item.AssetId, cancellationToken);
        if (existing is not null)
        {
            LogDuplicatePrize(batch.Lot, command.SeatNo, command.Item.AssetId);
            return new OpeningResultApplied(false, existing.Id);
        }

        SeatStateMachine.RequireTransition(seat.Status, SeatStatus.Opened);
        seat.Status = SeatStatus.Opened;
        seat.PrizeAssetId = command.Item.AssetId;
        seat.HistoryId = command.HistoryId ?? seat.HistoryId;

        var prize = new UserPrize
        {
            Id = Guid.CreateVersion7(),
            UserId = order.UserId,
            OrderId = order.Id,
            Source = MapSource(order.ServiceMode),
            AssetId = command.Item.AssetId,
            ClassId = command.Item.ClassId,
            InstanceId = command.Item.InstanceId,
            MarketHashName = command.Item.MarketHashName,
            Quality = command.Item.Quality,
            Exterior = command.Item.Exterior,
            FloatValue = command.Item.FloatValue,
            Rarity = command.Item.Rarity,
            IconUrl = command.Item.IconUrl,
            InspectLink = command.Item.InspectLink,
            PriceCents = command.Item.PriceCents,
            TradableAtUtc = command.Item.TradableAtUtc
        };
        db.UserPrizes.Add(prize);
        db.UserPrizeLogs.Add(new UserPrizeLog
        {
            PrizeId = prize.Id,
            UserId = order.UserId,
            OrderId = order.Id,
            Source = prize.Source,
            RawJson = command.RawJson
        });

        await db.SaveChangesAsync(cancellationToken);
        LogPrizeCreated(batch.Lot, command.SeatNo, order.OrderNo, command.Item.AssetId);
        return new OpeningResultApplied(true, prize.Id);
    }

    /// <summary>
    /// Moves a seat one step along the post-open chain (stock fetch, trading, completion) as
    /// inventory/trade events arrive. Jumps and backwards moves are rejected by the machine.
    /// </summary>
    public async Task<int> AdvanceSeatAsync(Guid batchId, int seatNo, SeatStatus target, CancellationToken cancellationToken = default)
    {
        var (batch, seat) = await LoadSeatAsync(batchId, seatNo, cancellationToken);

        SeatStateMachine.RequireTransition(seat.Status, target);
        seat.Status = target;
        await db.SaveChangesAsync(cancellationToken);
        LogSeatAdvanced(batch.Lot, seatNo, target);
        return seatNo;
    }

    private static PrizeSource MapSource(ServiceMode mode) => mode switch
    {
        // Batch seats open on a platform account; the legacy flow calls this 团购代开.
        ServiceMode.SelfOpen => PrizeSource.SelfOpen,
        _ => PrizeSource.GroupOpen
    };

    private async Task<(GroupBuying Batch, GroupBuyingSeat Seat)> LoadSeatAsync(
        Guid batchId, int seatNo, CancellationToken cancellationToken)
    {
        var batch = await db.GroupBuyings
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new BatchNotFoundException($"Batch {batchId} was not found.");

        var seat = await db.GroupBuyingSeats
            .FirstOrDefaultAsync(s => s.GroupBuyingId == batchId && s.SeatNo == seatNo, cancellationToken)
            ?? throw new SeatNotFoundException($"Seat {seatNo} of batch {batchId} was not found.");

        return (batch, seat);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "batch {Lot} created for product {ProductId} with {SeatCount} seats")]
    private partial void LogBatchCreated(Guid productId, string lot, int seatCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "seat {SeatNo} of batch {Lot} claimed for order {OrderId}")]
    private partial void LogSeatClaimed(string lot, int seatNo, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "seat {SeatNo} of batch {Lot} released back to sale")]
    private partial void LogSeatReleased(string lot, int seatNo);

    [LoggerMessage(Level = LogLevel.Information, Message = "seat {SeatNo} of batch {Lot} marked paid")]
    private partial void LogSeatPaid(string lot, int seatNo);

    [LoggerMessage(Level = LogLevel.Information, Message = "opening submitted for seat {SeatNo} of batch {Lot} (job {JobId})")]
    private partial void LogOpeningSubmitted(string lot, int seatNo, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "opening dispatch failed for seat {SeatNo} of batch {Lot}; seat returned to paid")]
    private partial void LogOpeningDispatchFailed(string lot, int seatNo);

    [LoggerMessage(Level = LogLevel.Information, Message = "opening result for seat {SeatNo} of batch {Lot} was already applied (asset {AssetId})")]
    private partial void LogDuplicatePrize(string lot, int seatNo, string assetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "prize {AssetId} recorded for order {OrderNo} (seat {SeatNo} of batch {Lot})")]
    private partial void LogPrizeCreated(string lot, int seatNo, string orderNo, string assetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "seat {SeatNo} of batch {Lot} advanced to {Status}")]
    private partial void LogSeatAdvanced(string lot, int seatNo, SeatStatus status);
}

/// <summary>
/// case_open dispatch payload. Keys are the exact camelCase names vapor's CaseOpenAction
/// reads: caseId resolves the case catalog entry, and explicit asset ids switch the action
/// to the real game-coordinator path (one crate + one key burned per dispatch).
/// </summary>
internal sealed record CaseOpenDispatchPayload(
    [property: JsonPropertyName("caseId")] string CaseId,
    [property: JsonPropertyName("crateAssetId")] string CrateAssetId,
    [property: JsonPropertyName("keyAssetId")] string KeyAssetId);
