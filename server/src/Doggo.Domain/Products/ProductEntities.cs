using Doggo.Domain.Common;

namespace Doggo.Domain.Products;

public enum ProductType
{
    Key = 0,
    Crate = 1,
    CrateComponent = 2
}

/// <summary>On-shelf mode: which purchase flows the product participates in.</summary>
public enum ProductStatus
{
    OffShelf = 0,
    SelfOpenOnly = 1,
    GroupOpenOnly = 2,
    OnShelf = 3
}

public enum CaseQuality
{
    Consumer = 1,
    Industrial = 2,
    MilSpec = 3,
    Restricted = 4,
    Classified = 5,
    Covert = 6,
    RareSpecial = 7
}

public class Product : Entity, ISoftDeletable
{
    public ProductType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;

    // Prices in cents. FinalPrice = max(ReferencePrice, PurchasePrice) + AdjustPrice.
    public long PurchasePriceCents { get; set; }
    public long ReferencePriceCents { get; set; }
    public long AdjustPriceCents { get; set; }
    public long FinalPriceCents { get; set; }
    public long RegularPriceCents { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.OffShelf;
    public string PlanFlags { get; set; } = string.Empty;
    public string? BuyNotice { get; set; }
    public int SortOrder { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public class CasePlan : Entity
{
    public Guid ProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int MinQuantity { get; set; } = 1;
    public int MaxQuantity { get; set; } = 1;
    public bool CanUseCoupon { get; set; }
    public bool CanUseCoin { get; set; }

    /// <summary>Cashback ratio in basis points (e.g. 500 = 5%).</summary>
    public int CashbackBps { get; set; }
    public bool Enabled { get; set; } = true;
}

public class CasePlanOption : Entity
{
    public Guid PlanId { get; set; }

    /// <summary>Price uplift ratio in basis points applied on top of crate+key cost.</summary>
    public int UpliftBps { get; set; }
    public int Quantity { get; set; } = 1;
    public int MinQuantity { get; set; } = 1;
    public long PriceCents { get; set; }
    public bool Enabled { get; set; } = true;
}

public class PromotionPlan : Entity
{
    public Guid ProductId { get; set; }
    public long DiscountPriceCents { get; set; }
    public int Stock { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public bool DailyReset { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public bool Enabled { get; set; } = true;
}

public class QuantityOption : Entity
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public long PriceCents { get; set; }

    /// <summary>Stock level at which an alarm is raised.</summary>
    public int AlarmThreshold { get; set; }
}

/// <summary>Display-only case content item; never used for drop decisions.</summary>
public class CaseContent : Entity
{
    public Guid ProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public CaseQuality Quality { get; set; }
    public string Color { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public long ReferencePriceCents { get; set; }
    public int SortOrder { get; set; }
}

public class ProductRecommend : Entity
{
    public Guid ProductId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ExchangeRate : Entity
{
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string Source { get; set; } = string.Empty;
}
