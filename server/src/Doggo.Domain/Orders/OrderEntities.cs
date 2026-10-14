using Doggo.Domain.Common;

namespace Doggo.Domain.Orders;

public enum OrderEntry
{
    Campaign = 0,
    Case = 1,
    Key = 2,
    Taobao = 3,
    Group = 4
}

public enum OrderStatus
{
    PendingPayment = 0,
    PendingSettlement = 1,
    PendingRetrieval = 2,
    Retrieving = 3,
    Completed = 4,
    Cancelled = 5,
    Refunded = 6
}

public enum ServiceMode
{
    SelfOpen = 0,
    CloudOpen = 1
}

public enum FulfillmentType
{
    Spot = 1,
    Cooldown = 2,
    KeyPurchase = 3
}

public enum PaymentProvider
{
    None = 0,
    Alipay = 1,
    WeChat = 2,
    UnionPay = 3,
    Coin = 4,
    Coupon = 5
}

public enum PaymentStatus
{
    Created = 0,
    Success = 1,
    Failed = 2,
    Refunded = 3
}

public class UserOrder : Entity
{
    public Guid UserId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public OrderEntry Entry { get; set; }
    public Guid ProductId { get; set; }
    public Guid? PlanId { get; set; }

    public long RegularPriceCents { get; set; }
    public long PriceCents { get; set; }
    public long TotalPriceCents { get; set; }
    public long TotalCostCents { get; set; }
    public long CoinDeduction { get; set; }
    public long CouponDeductionCents { get; set; }
    public long CashbackCents { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public ServiceMode ServiceMode { get; set; }
    public FulfillmentType FulfillmentType { get; set; }
    public Guid? TradeUrlId { get; set; }

    /// <summary>Promotion whose limited stock this order claimed (null when bought at base price).</summary>
    public Guid? PromotionId { get; set; }

    /// <summary>Official Steam trade offer id once vapor accepted the retrieval dispatch.</summary>
    public string? TradeOfferId { get; set; }

    public string? Error { get; set; }

    public PaymentProvider PayType { get; set; } = PaymentProvider.None;
    public string? PayTradeNo { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? PayDeadlineUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class PaymentRecord : Entity
{
    public Guid OrderId { get; set; }
    public PaymentProvider Provider { get; set; }
    public string ProviderTradeNo { get; set; } = string.Empty;

    /// <summary>Deduplicates provider callbacks.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
    public string? CallbackPayload { get; set; }
    public DateTime? PaidAtUtc { get; set; }
}

public class RefundRecord : Entity
{
    public Guid OrderId { get; set; }
    public PaymentProvider Provider { get; set; }
    public string ProviderRefundNo { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Created;
}

/// <summary>
/// Non-secret payment channel configuration. Secrets never land in this table:
/// <see cref="SecretRef"/> names the environment variable that holds the provider
/// key, matching the repo rule that sensitive configuration stays env-only.
/// </summary>
public class PaymentChannel : Entity
{
    public PaymentProvider Provider { get; set; }
    public string MerchantId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;

    /// <summary>Channel fee in basis points (e.g. 60 = 0.6%).</summary>
    public int FeeBps { get; set; }

    public string SecretRef { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Note { get; set; }
}
