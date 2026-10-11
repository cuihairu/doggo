using Doggo.Domain.Products;

namespace Doggo.Domain.Tests;

public class PriceCalculatorTests
{
    private static Product Product(long final = 0, long purchase = 1000, long reference = 1200, long adjust = 100) =>
        new()
        {
            FinalPriceCents = final,
            PurchasePriceCents = purchase,
            ReferencePriceCents = reference,
            AdjustPriceCents = adjust
        };

    [Fact]
    public void Effective_unit_price_composes_max_and_adjust()
    {
        // max(1200, 1000) + 100 = 1300
        Assert.Equal(1300, PriceCalculator.EffectiveUnitPriceCents(Product()));

        // reference below purchase → purchase wins: max(1000, 900) + 200 = 1200
        Assert.Equal(1200, PriceCalculator.EffectiveUnitPriceCents(Product(purchase: 1000, reference: 900, adjust: 200)));
    }

    [Fact]
    public void Stored_final_price_wins_when_set()
    {
        Assert.Equal(1500, PriceCalculator.EffectiveUnitPriceCents(Product(final: 1500)));
    }

    [Fact]
    public void Calculate_plain_uses_final_price()
    {
        var breakdown = PriceCalculator.Calculate(Product(final: 1500), 3);

        Assert.Equal(1500, breakdown.UnitPriceCents);
        Assert.Equal(3, breakdown.Quantity);
        Assert.Equal(4500, breakdown.TotalCents);
        Assert.Equal(PriceSource.FinalPrice, breakdown.PriceSource);
    }

    [Fact]
    public void Calculate_quantity_below_threshold_ignores_tier()
    {
        var tiers = new List<QuantityOption> { new() { Quantity = 5, PriceCents = 1200 } };

        var breakdown = PriceCalculator.Calculate(Product(final: 1500), 4, tiers);

        Assert.Equal(1500, breakdown.UnitPriceCents);
        Assert.Equal(PriceSource.FinalPrice, breakdown.PriceSource);
    }

    [Fact]
    public void Calculate_applies_cheapest_met_tier()
    {
        var tiers = new List<QuantityOption>
        {
            new() { Quantity = 5, PriceCents = 1400 },
            new() { Quantity = 10, PriceCents = 1250 },
            new() { Quantity = 20, PriceCents = 1100 }
        };

        var at5 = PriceCalculator.Calculate(Product(final: 1500), 5, tiers);
        var at10 = PriceCalculator.Calculate(Product(final: 1500), 10, tiers);
        var at50 = PriceCalculator.Calculate(Product(final: 1500), 50, tiers);

        Assert.Equal((1400, PriceSource.QuantityTier, 7000L), (at5.UnitPriceCents, at5.PriceSource, at5.TotalCents));
        Assert.Equal(1250, at10.UnitPriceCents);
        Assert.Equal(1100, at50.UnitPriceCents);
    }

    [Fact]
    public void Calculate_ignores_tiers_not_actually_cheaper()
    {
        var tiers = new List<QuantityOption> { new() { Quantity = 2, PriceCents = 2000 } };

        var breakdown = PriceCalculator.Calculate(Product(final: 1500), 10, tiers);

        Assert.Equal(1500, breakdown.UnitPriceCents);
        Assert.Equal(PriceSource.FinalPrice, breakdown.PriceSource);
    }

    [Fact]
    public void Calculate_plan_option_prices_per_unit_times_quantity()
    {
        var option = new CasePlanOption { PriceCents = 3250, Quantity = 3 };

        var breakdown = PriceCalculator.Calculate(Product(final: 1500), option.Quantity, planOption: option);

        Assert.Equal(3250, breakdown.UnitPriceCents);
        Assert.Equal(9750, breakdown.TotalCents);
        Assert.Equal(PriceSource.PlanOption, breakdown.PriceSource);
    }

    [Fact]
    public void Calculate_plan_option_falls_back_to_final_price_when_unset()
    {
        var option = new CasePlanOption { PriceCents = 0, Quantity = 1 };

        var breakdown = PriceCalculator.Calculate(Product(final: 1500), 1, planOption: option);

        Assert.Equal(1500, breakdown.UnitPriceCents);
    }

    [Theory]
    [InlineData(10000, 500, 500)]   // 5% of 100元
    [InlineData(9999, 0, 0)]
    [InlineData(10000, 10000, 10000)] // 100%
    [InlineData(12345, 350, 432)]   // floor division
    public void Cashback_converts_bps_to_cents(long total, int bps, long expected)
    {
        Assert.Equal(expected, PriceCalculator.CashbackCents(total, bps));
    }
}
