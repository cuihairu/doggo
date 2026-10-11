namespace Doggo.Domain.Products;

/// <summary>How the unit price for a calculation was determined.</summary>
public static class PriceSource
{
    /// <summary>Stored/computed final price of the product itself.</summary>
    public const string FinalPrice = "final_price";

    /// <summary>A case plan option's stored per-unit price (already includes its uplift).</summary>
    public const string PlanOption = "plan_option";

    /// <summary>A quantity tier from the product's ladder undercut the base price.</summary>
    public const string QuantityTier = "quantity_tier";
}

public sealed record PriceBreakdown(long UnitPriceCents, int Quantity, long TotalCents, string PriceSource);

/// <summary>
/// Pure pricing rules: final price composition, quantity-tier ladders and case plan options.
/// No data access — callers load the product and its options and pass them in.
/// </summary>
public static class PriceCalculator
{
    /// <summary>FinalPrice = max(ReferencePrice, PurchasePrice) + AdjustPrice; stored FinalPrice wins when set.</summary>
    public static long EffectiveUnitPriceCents(Product product)
    {
        if (product.FinalPriceCents > 0)
        {
            return product.FinalPriceCents;
        }

        return Math.Max(product.ReferencePriceCents, product.PurchasePriceCents) + product.AdjustPriceCents;
    }

    /// <summary>
    /// Resolves the unit price for a quantity: the cheapest applicable rung of the ladder.
    /// A tier only applies when its quantity threshold is met and its stored unit price is
    /// strictly lower than what a smaller rung (or the base price) would give.
    /// </summary>
    public static long ResolveTierUnitPriceCents(Product product, IReadOnlyList<QuantityOption> tiers, int quantity)
    {
        var unit = EffectiveUnitPriceCents(product);

        foreach (var tier in tiers)
        {
            if (tier.Quantity <= 0 || tier.Quantity > quantity || tier.PriceCents <= 0)
            {
                continue;
            }

            if (tier.PriceCents < unit)
            {
                unit = tier.PriceCents;
            }
        }

        return unit;
    }

    /// <summary>
    /// Prices a purchase. Plan options win over the ladder (they price crate+key bundles);
    /// otherwise the quantity ladder applies; otherwise the plain final price.
    /// </summary>
    public static PriceBreakdown Calculate(
        Product product,
        int quantity,
        IReadOnlyList<QuantityOption>? quantityTiers = null,
        CasePlanOption? planOption = null)
    {
        var count = Math.Max(quantity, 1);

        if (planOption is not null)
        {
            var optionUnit = planOption.PriceCents > 0
                ? planOption.PriceCents
                : EffectiveUnitPriceCents(product);
            return new PriceBreakdown(optionUnit, count, optionUnit * count, PriceSource.PlanOption);
        }

        if (quantityTiers is { Count: > 0 })
        {
            var tierUnit = ResolveTierUnitPriceCents(product, quantityTiers, count);
            var baseUnit = EffectiveUnitPriceCents(product);
            var source = tierUnit < baseUnit ? PriceSource.QuantityTier : PriceSource.FinalPrice;
            return new PriceBreakdown(tierUnit, count, tierUnit * count, source);
        }

        var unit = EffectiveUnitPriceCents(product);
        return new PriceBreakdown(unit, count, unit * count, PriceSource.FinalPrice);
    }

    /// <summary>Cashback payout in cents for a plan purchase (basis points of the total).</summary>
    public static long CashbackCents(long totalCents, int cashbackBps) =>
        totalCents * cashbackBps / 10_000;
}
