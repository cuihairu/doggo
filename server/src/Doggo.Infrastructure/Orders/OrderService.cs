using System.Text.Json;
using System.Text.Json.Serialization;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Orders;

public sealed class OrderNotFoundException(string message) : Exception(message)
{
}

public sealed class OrderValidationException(string message) : Exception(message)
{
}

public sealed record CreateOrderCommand(
    Guid UserId,
    Guid ProductId,
    Guid? PlanId,
    Guid? OptionId,
    OrderEntry Entry,
    ServiceMode ServiceMode,
    FulfillmentType FulfillmentType,
    PaymentProvider PayType,
    long CoinDeduction = 0,
    Guid? UserCouponId = null);

public sealed record CreateOrderResult(
    Guid OrderId,
    string OrderNo,
    long TotalPriceCents,
    long TotalCostCents,
    long CashbackCents,
    bool PromotionApplied,
    DateTime PayDeadlineUtc);

/// <summary>
/// Order lifecycle: creation with price assembly and promotion stock occupancy, payment
/// callbacks with idempotency, cancellation with stock restore, refunds and the sweeps the
/// workers drive. Every state move re-reads the order and checks the status before writing,
/// and payment records carry a unique idempotency key, so callback replays and worker sweeps
/// collapse into no-ops. Transitions are single SaveChanges writes; a callback and a sweep
/// racing on the same row still settle to a coherent state because the sweep refuses to
/// cancel an order that already has a successful payment record.
/// </summary>
public sealed partial class OrderService(
    DoggoDbContext db,
    IOptions<OrderOptions> options,
    IPaymentProvider payment,
    VaporClient vapor,
    Marketing.CouponService coupons,
    Marketing.CoinService coins,
    ILogger<OrderService> logger)
{
    private readonly OrderOptions _options = options.Value;

    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            ?? throw new OrderNotFoundException($"Product {command.ProductId} was not found.");

        if (product.Status == ProductStatus.OffShelf)
        {
            throw new OrderValidationException($"Product {product.Name} is off shelf.");
        }

        ValidateServiceMode(product.Status, command.ServiceMode);

        var planOption = await LoadPlanOptionAsync(command, cancellationToken);
        var plan = command.PlanId is null
            ? null
            : await db.CasePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PlanId.Value, cancellationToken);
        var tiers = await db.QuantityOptions.AsNoTracking()
            .Where(tier => tier.ProductId == command.ProductId)
            .ToListAsync(cancellationToken);

        var quantity = planOption?.Quantity ?? 1;
        var breakdown = PriceCalculator.Calculate(product, quantity, tiers, planOption);
        var total = breakdown.TotalCents;

        // Limited promotions occupy stock at order time so a burst cannot oversell; cancelled
        // and expired orders give the unit back through CancelUnpaidAsync.
        var promotion = await ClaimPromotionAsync(command, cancellationToken);
        if (promotion is not null)
        {
            total = promotion.DiscountPriceCents * quantity;
        }

        var cashback = PriceCalculator.CashbackCents(total, plan?.CashbackBps ?? 0);

        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId,
            OrderNo = NewOrderNo(now),
            Entry = command.Entry,
            ProductId = command.ProductId,
            PlanId = command.PlanId,
            PromotionId = promotion?.Id,
            RegularPriceCents = product.RegularPriceCents,
            PriceCents = breakdown.UnitPriceCents,
            TotalPriceCents = total,
            TotalCostCents = total,
            CashbackCents = cashback,
            Status = OrderStatus.PendingPayment,
            ServiceMode = command.ServiceMode,
            FulfillmentType = command.FulfillmentType,
            PayType = command.PayType,
            PayDeadlineUtc = now.AddSeconds(Math.Max(_options.PayTimeoutSeconds, 1))
        };
        db.UserOrders.Add(order);

        // The coupon is consumed first so an invalid coupon never burns coins; a coin spend
        // that then fails gives the coupon back before the rejection surfaces.
        if (command.UserCouponId is { } userCouponId)
        {
            order.CouponDeductionCents = await coupons.ApplyCouponAsync(
                command.UserId, userCouponId, command.ProductId, total, order.Id, cancellationToken);
        }

        if (command.CoinDeduction > 0)
        {
            try
            {
                // SpendAsync returns the remaining balance; the deduction is the requested amount.
                await coins.SpendAsync(
                    command.UserId, command.CoinDeduction, "order", order.Id.ToString(), cancellationToken);
                order.CoinDeduction = command.CoinDeduction;
            }
            catch
            {
                await coupons.RestoreCouponAsync(order.Id, cancellationToken);
                throw;
            }
        }

        order.TotalCostCents = Math.Max(total - order.CoinDeduction - order.CouponDeductionCents, 0);

        await db.SaveChangesAsync(cancellationToken);

        LogOrderCreated(logger, order.OrderNo, command.UserId, promotion is not null);
        return new CreateOrderResult(order.Id, order.OrderNo, total, order.TotalCostCents, cashback, promotion is not null, order.PayDeadlineUtc!.Value);
    }

    /// <summary>Asks the payment channel for a pay URL; only unpaid orders qualify.</summary>
    public async Task<PaymentCreation> CreatePaymentAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new OrderValidationException($"Order {order.OrderNo} is {order.Status}, not awaiting payment.");
        }

        return await payment.CreatePaymentAsync(order, cancellationToken);
    }

    /// <summary>
    /// Applies a verified payment callback. The idempotency key is accepted once and the order
    /// only settles while still pending payment, so duplicate deliveries are no-ops.
    /// </summary>
    public async Task<bool> ApplyPaymentCallbackAsync(
        Guid orderId,
        PaymentCallbackResult callback,
        string? payload,
        CancellationToken cancellationToken = default)
    {
        if (await OrderAlreadyPaidAsync(callback.IdempotencyKey, cancellationToken))
        {
            LogDuplicateCallback(logger, callback.IdempotencyKey);
            return false;
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (!callback.Success || order.Status != OrderStatus.PendingPayment)
        {
            db.PaymentRecords.Add(new PaymentRecord
            {
                Id = Guid.CreateVersion7(),
                OrderId = orderId,
                Provider = payment.Provider,
                ProviderTradeNo = callback.ProviderTradeNo,
                IdempotencyKey = callback.IdempotencyKey,
                AmountCents = callback.AmountCents,
                Status = PaymentStatus.Failed,
                CallbackPayload = payload ?? callback.Message
            });
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }

        var now = DateTime.UtcNow;
        order.Status = OrderStatus.PendingSettlement;
        order.PayType = payment.Provider;
        order.PayTradeNo = callback.ProviderTradeNo;
        order.PaidAtUtc = now;

        db.PaymentRecords.Add(new PaymentRecord
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Provider = payment.Provider,
            ProviderTradeNo = callback.ProviderTradeNo,
            IdempotencyKey = callback.IdempotencyKey,
            AmountCents = callback.AmountCents,
            Status = PaymentStatus.Success,
            CallbackPayload = payload,
            PaidAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        LogPaymentApplied(logger, order.OrderNo, callback.AmountCents);
        return true;
    }

    /// <summary>Cancels an unpaid order and returns the promotion unit it held.</summary>
    public async Task<bool> CancelUnpaidAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!await CanCancelOrderAsync(order, cancellationToken))
        {
            return false;
        }

        order.Status = OrderStatus.Cancelled;
        order.Error = reason;
        await db.SaveChangesAsync(cancellationToken);

        await RestorePromotionAsync(order.PromotionId, cancellationToken);
        await RestoreDeductionsAsync(order, "order_cancel", cancellationToken);
        return true;
    }

    /// <summary>Cancels every unpaid order past its payment deadline.</summary>
    public async Task<int> ExpireUnpaidOrdersAsync(DateTime? thresholdUtc = null, CancellationToken cancellationToken = default)
    {
        var threshold = thresholdUtc ?? DateTime.UtcNow;
        var expired = await db.UserOrders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingPayment
                        && o.PayDeadlineUtc != null
                        && o.PayDeadlineUtc <= threshold)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var orderId in expired)
        {
            if (await CancelUnpaidAsync(orderId, "payment timed out", cancellationToken))
            {
                count++;
            }
        }

        if (count > 0)
        {
            LogExpired(logger, count);
        }

        return count;
    }

    /// <summary>Refunds a paid order through the payment channel and records it.</summary>
    public async Task<bool> RefundOrderAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.Refunded))
        {
            return false;
        }

        var previousStatus = order.Status;
        var refunded = await payment.RefundAsync(order.PayTradeNo ?? string.Empty, order.TotalCostCents, reason, cancellationToken);
        if (!refunded)
        {
            LogRefundRejected(logger, order.OrderNo);
            return false;
        }

        // A concurrent callback or retrieval move makes the refund obsolete.
        var fresh = await db.UserOrders.FirstAsync(o => o.Id == orderId, cancellationToken);
        if (fresh.Status != previousStatus)
        {
            return false;
        }

        order.Status = OrderStatus.Refunded;
        order.Error = reason;

        db.RefundRecords.Add(new RefundRecord
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Provider = payment.Provider,
            ProviderRefundNo = $"R-{order.OrderNo}",
            AmountCents = order.TotalCostCents,
            Reason = reason,
            Status = PaymentStatus.Refunded
        });
        await db.SaveChangesAsync(cancellationToken);

        await RestoreDeductionsAsync(order, "order_refund", cancellationToken);
        LogRefunded(logger, order.OrderNo, reason);
        return true;
    }

    /// <summary>
    /// Gives a consumed coupon and the spent coins back after a cancel or refund. Failures
    /// here must not fail the cancel/refund itself: the ledger movement is best effort and a
    /// missed restore can be replayed safely (both restores are idempotent).
    /// </summary>
    private async Task RestoreDeductionsAsync(UserOrder order, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await coupons.RestoreCouponAsync(order.Id, cancellationToken);
            if (order.CoinDeduction > 0)
            {
                await coins.RefundAsync(order.UserId, order.CoinDeduction, reason, order.Id.ToString(), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            LogRestoreFailed(logger, ex, order.OrderNo, reason);
        }
    }

    /// <summary>Auto-refunds orders stuck after settlement or awaiting retrieval too long.</summary>
    public async Task<int> RefundStuckOrdersAsync(DateTime? thresholdUtc = null, CancellationToken cancellationToken = default)
    {
        var threshold = thresholdUtc ?? DateTime.UtcNow.AddMinutes(-_options.SettlementStuckMinutes);
        var stuck = await db.UserOrders.AsNoTracking()
            .Where(o => (o.Status == OrderStatus.PendingSettlement || o.Status == OrderStatus.PendingRetrieval)
                        && o.UpdatedAtUtc <= threshold)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var orderId in stuck)
        {
            if (await RefundOrderAsync(orderId, "stuck order auto refund", cancellationToken))
            {
                count++;
            }
        }

        if (count > 0)
        {
            LogStuckRefunded(logger, count);
        }

        return count;
    }

    /// <summary>Rolls retrievals that never finished back to PendingRetrieval for a retry.</summary>
    public async Task<int> RollbackStuckRetrievalsAsync(DateTime? thresholdUtc = null, CancellationToken cancellationToken = default)
    {
        var threshold = thresholdUtc ?? DateTime.UtcNow.AddMinutes(-_options.RetrievalStuckMinutes);
        var candidates = await db.UserOrders
            .Where(o => o.Status == OrderStatus.Retrieving && o.UpdatedAtUtc <= threshold)
            .ToListAsync(cancellationToken);

        foreach (var order in candidates)
        {
            order.Status = OrderStatus.PendingRetrieval;
            order.Error = "retrieval stuck, rolled back for retry";
        }

        if (candidates.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            LogRetrievalRolledBack(logger, candidates.Count);
        }

        return candidates.Count;
    }

    /// <summary>
    /// Starts retrieval for a paid order: PendingSettlement/PendingRetrieval → Retrieving,
    /// then dispatches a vapor trade offer. Failures roll the order back to PendingRetrieval
    /// with the error recorded for the retry policy.
    /// </summary>
    public async Task<(bool Accepted, string? OfferId, string? Error)> SubmitRetrievalAsync(
        Guid orderId,
        Guid tradeUrlId,
        IReadOnlyList<VaporTradeAsset> itemsToGive,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status is not (OrderStatus.PendingSettlement or OrderStatus.PendingRetrieval))
        {
            return (false, null, $"Order {order.OrderNo} is {order.Status} and cannot start retrieval.");
        }

        var tradeUrl = await db.UserTradeUrls.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tradeUrlId && t.UserId == order.UserId, cancellationToken);
        if (tradeUrl is null)
        {
            return (false, null, "Trade URL was not found for this user.");
        }

        if (tradeUrl.Status == TradeUrlStatus.Invalid)
        {
            return (false, null, "Trade URL failed validation.");
        }

        if (!vapor.IsConfigured)
        {
            return (false, null, "vapor is not configured; retrieval is unavailable.");
        }

        // Move to Retrieving before the dispatch so a concurrent sweep cannot refund the
        // order mid-flight; the failure paths below roll it back.
        order.Status = OrderStatus.Retrieving;
        order.TradeUrlId = tradeUrl.Id;
        await db.SaveChangesAsync(cancellationToken);

        string? offerId;
        try
        {
            offerId = await DispatchTradeOfferAsync(tradeUrl, order, itemsToGive, cancellationToken);
        }
        catch (VaporClientException ex)
        {
            await RollbackRetrievalAsync(order, $"vapor dispatch failed: {ex.Message}", cancellationToken);
            return (false, null, $"vapor dispatch failed: {ex.Message}");
        }

        if (offerId is null)
        {
            await RollbackRetrievalAsync(order, "vapor returned no trade offer id", cancellationToken);
            return (false, null, "vapor returned no trade offer id");
        }

        order.TradeOfferId = offerId;
        await db.SaveChangesAsync(cancellationToken);

        LogRetrievalSubmitted(logger, order.OrderNo, offerId);
        return (true, offerId, null);
    }

    /// <summary>Completes an order whose trade offer was accepted (callback driven).</summary>
    public async Task<bool> CompleteRetrievalAsync(Guid orderId, string offerId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.Retrieving)
        {
            return false;
        }

        order.Status = OrderStatus.Completed;
        order.TradeOfferId = offerId;
        order.CompletedAtUtc = DateTime.UtcNow;
        order.Error = null;
        await db.SaveChangesAsync(cancellationToken);

        LogOrderCompleted(logger, order.OrderNo, offerId);
        return true;
    }

    /// <summary>Rolls a failed retrieval callback back so the order can be retried or refunded.</summary>
    public async Task<bool> FailRetrievalAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.Retrieving)
        {
            return false;
        }

        order.Status = OrderStatus.PendingRetrieval;
        order.Error = reason;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<UserOrder> FindOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.UserOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
        ?? throw new OrderNotFoundException($"Order {orderId} was not found.");

    private async Task<UserOrder> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.UserOrders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
        ?? throw new OrderNotFoundException($"Order {orderId} was not found.");

    private Task<bool> OrderAlreadyPaidAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.PaymentRecords.AsNoTracking().AnyAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    /// <summary>
    /// An order can only be cancelled while still awaiting payment, and never once a payment
    /// record has settled — otherwise a late callback would leave a charged order cancelled
    /// with no refund path.
    /// </summary>
    private async Task<bool> CanCancelOrderAsync(UserOrder order, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.PendingPayment
            || !OrderStateMachine.CanTransition(order.Status, OrderStatus.Cancelled))
        {
            return false;
        }

        return !await db.PaymentRecords.AsNoTracking()
            .AnyAsync(r => r.OrderId == order.Id && r.Status == PaymentStatus.Success, cancellationToken);
    }

    private async Task<PromotionPlan?> ClaimPromotionAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var promotion = await db.PromotionPlans
            .Where(p => p.ProductId == command.ProductId
                        && p.Enabled
                        && p.Stock > 0
                        && (p.StartAtUtc == null || p.StartAtUtc <= now)
                        && (p.EndAtUtc == null || p.EndAtUtc >= now))
            .OrderBy(p => p.DiscountPriceCents)
            .FirstOrDefaultAsync(cancellationToken);

        if (promotion is null || promotion.Stock <= 0)
        {
            return null;
        }

        if (promotion.PerUserLimit > 0)
        {
            var used = await db.UserOrders.AsNoTracking()
                .CountAsync(o => o.UserId == command.UserId
                                 && o.PromotionId == promotion.Id
                                 && o.Status != OrderStatus.Cancelled, cancellationToken);
            if (used >= promotion.PerUserLimit)
            {
                return null;
            }
        }

        promotion.Stock--;
        await db.SaveChangesAsync(cancellationToken);
        return promotion;
    }

    private async Task RestorePromotionAsync(Guid? promotionId, CancellationToken cancellationToken)
    {
        if (promotionId is null)
        {
            return;
        }

        var promotion = await db.PromotionPlans.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion is null)
        {
            return;
        }

        promotion.Stock++;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RollbackRetrievalAsync(UserOrder order, string reason, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Retrieving)
        {
            return;
        }

        order.Status = OrderStatus.PendingRetrieval;
        order.Error = reason;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Runs send_trade_offer and extracts the official trade offer id.</summary>
    private async Task<string?> DispatchTradeOfferAsync(
        UserTradeUrl tradeUrl,
        UserOrder order,
        IReadOnlyList<VaporTradeAsset> itemsToGive,
        CancellationToken cancellationToken)
    {
        var job = await vapor.DispatchAndWaitAsync("send_trade_offer", new TradeOfferPayload(
            tradeUrl.Url,
            $"doggo order {order.OrderNo}",
            [.. itemsToGive.Select(asset => new TradeOfferAsset(asset.AssetId, asset.AppId, asset.ClassId, asset.InstanceId))]),
            cancellationToken: cancellationToken);

        var tasks = job.Tasks ?? [];
        var failed = tasks.FirstOrDefault(t => !VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Finished));
        if (failed is not null)
        {
            throw new VaporClientException($"vapor task failed: {failed.Error ?? failed.Status}");
        }

        var output = tasks.Select(t => t.Output).FirstOrDefault(o => o is { ValueKind: JsonValueKind.Object });
        if (output is { ValueKind: JsonValueKind.Object }
            && output.Value.TryGetProperty("trade_offer_id", out var offerId)
            && offerId.GetString() is { Length: > 0 } id)
        {
            return id;
        }

        return null;
    }

    private async Task<CasePlanOption?> LoadPlanOptionAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        if (command.OptionId is null)
        {
            return null;
        }

        if (command.PlanId is null)
        {
            throw new OrderValidationException("A plan option requires a plan.");
        }

        var option = await db.CasePlanOptions.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == command.OptionId.Value, cancellationToken)
            ?? throw new OrderValidationException($"Plan option {command.OptionId} was not found.");

        if (!option.Enabled)
        {
            throw new OrderValidationException($"Plan option {command.OptionId} is disabled.");
        }

        if (option.PlanId != command.PlanId.Value)
        {
            throw new OrderValidationException($"Plan option {command.OptionId} does not belong to plan {command.PlanId}.");
        }

        // The plan is what prices the bundle, so it must be the one belonging to this product.
        var plan = await db.CasePlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PlanId.Value, cancellationToken)
            ?? throw new OrderValidationException($"Plan {command.PlanId} was not found.");

        if (!plan.Enabled || plan.ProductId != command.ProductId)
        {
            throw new OrderValidationException($"Plan {command.PlanId} is not available for this product.");
        }

        return option;
    }

    private static void ValidateServiceMode(ProductStatus status, ServiceMode requested)
    {
        if (status == ProductStatus.SelfOpenOnly && requested != ServiceMode.SelfOpen)
        {
            throw new OrderValidationException("Product only supports self-open mode.");
        }

        if (status == ProductStatus.GroupOpenOnly && requested != ServiceMode.CloudOpen)
        {
            throw new OrderValidationException("Product only supports cloud-open mode.");
        }
    }

    private static string NewOrderNo(DateTime at) =>
        $"DO{at:yyyyMMddHHmmss}{Random.Shared.Next(100000, 999999)}";

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} created for user {UserId} (promotion claimed: {PromotionClaimed})")]
    private static partial void LogOrderCreated(ILogger logger, string orderNo, Guid userId, bool promotionClaimed);

    [LoggerMessage(Level = LogLevel.Information, Message = "payment callback {IdempotencyKey} was already applied")]
    private static partial void LogDuplicateCallback(ILogger logger, string idempotencyKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} payment applied ({AmountCents} cents)")]
    private static partial void LogPaymentApplied(ILogger logger, string orderNo, long amountCents);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} unpaid orders expired")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "refund rejected by provider for order {OrderNo}")]
    private static partial void LogRefundRejected(ILogger logger, string orderNo);

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} refunded: {Reason}")]
    private static partial void LogRefunded(ILogger logger, string orderNo, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "deduction restore failed for order {OrderNo} ({Reason})")]
    private static partial void LogRestoreFailed(ILogger logger, Exception ex, string orderNo, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} stuck orders refunded")]
    private static partial void LogStuckRefunded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} stuck retrievals rolled back")]
    private static partial void LogRetrievalRolledBack(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} retrieval submitted (offer {OfferId})")]
    private static partial void LogRetrievalSubmitted(ILogger logger, string orderNo, string offerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} completed (offer {OfferId})")]
    private static partial void LogOrderCompleted(ILogger logger, string orderNo, string offerId);
}

/// <summary>Asset reference for a trade offer, resolved from prize inventory by callers.</summary>
public sealed record VaporTradeAsset(string AssetId, string AppId, string ClassId, string InstanceId);

internal sealed record TradeOfferPayload(
    [property: JsonPropertyName("trade_url")] string TradeUrl,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("items_to_give")] TradeOfferAsset[] ItemsToGive);

internal sealed record TradeOfferAsset(
    [property: JsonPropertyName("assetid")] string AssetId,
    [property: JsonPropertyName("appid")] string AppId,
    [property: JsonPropertyName("classid")] string ClassId,
    [property: JsonPropertyName("instanceid")] string InstanceId);
