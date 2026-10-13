using Doggo.Domain.Analytics;

namespace Doggo.Domain.Tests;

/// <summary>The event dictionary gates /api/events ingest (design §4.4).</summary>
public class AnalyticsEventDictionaryTests
{
    [Fact]
    public void Knows_the_four_funnel_events()
    {
        var names = AnalyticsEventDictionary.KnownEventNames;

        Assert.Equal(4, names.Count);
        Assert.Contains("page_view_product", names);
        Assert.Contains("checkout_intent", names);
        Assert.Contains("order_created", names);
        Assert.Contains("order_paid", names);
    }

    [Fact]
    public void Unknown_name_or_schema_version_is_rejected()
    {
        Assert.True(AnalyticsEventDictionary.IsKnown("checkout_intent", 1));
        Assert.False(AnalyticsEventDictionary.IsKnown("page_view_home", 1));
        Assert.False(AnalyticsEventDictionary.IsKnown("checkout_intent", 2));
    }

    [Fact]
    public void Whitelists_cover_the_design_attributes_only()
    {
        Assert.True(AnalyticsEventDictionary.TryGetWhitelist("page_view_product", out var view));
        Assert.Equal(["productId", "source"], view.OrderBy(k => k).ToArray());

        Assert.True(AnalyticsEventDictionary.TryGetWhitelist("order_paid", out var paid));
        Assert.Equal(["amountCents", "orderId", "payType"], paid.OrderBy(k => k).ToArray());

        Assert.False(AnalyticsEventDictionary.TryGetWhitelist("page_view_home", out _));
    }
}
