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
