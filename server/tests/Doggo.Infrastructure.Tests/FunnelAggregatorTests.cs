using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

/// <summary>Funnel attribution rules: identity, 7-day window, day of first step, group key (design §7.3).</summary>
public class FunnelAggregatorTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 5);

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"funnel-aggregator-{Guid.NewGuid():N}")
            .Options);

    private static FunnelAggregator NewAggregator(DoggoDbContext db) =>
        new(db, NullLogger<FunnelAggregator>.Instance);

    private static DateTime At(DateOnly day, int hour, int minute = 0) =>
        new(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Utc);

    private static AnalyticsEvent Evt(
        string name,
        DateTime at,
        Guid? userId = null,
        string visitorKey = "",
        Guid sessionId = default,
        string channel = "organic",
        AnalyticsClient client = AnalyticsClient.Web,
        Dictionary<string, object?>? props = null)
    {
        var idempotencyKey = $"{name}:{Guid.NewGuid():N}";
        return new AnalyticsEvent
        {
            Id = Guid.CreateVersion7(),
            EventName = name,
            OccurredAtUtc = at,
            ReceivedAtUtc = at,
            SessionId = sessionId,
            UserId = userId,
            VisitorKey = visitorKey,
            Client = client,
            Channel = channel,
            PropertiesJson = props is null ? "{}" : System.Text.Json.JsonSerializer.Serialize(props),
            IdempotencyKey = idempotencyKey
        };
    }

    [Fact]
    public async Task Full_funnel_counts_every_step_for_one_identity()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 10), user, props: new() { ["productId"] = "p" }),
            Evt("checkout_intent", At(Day1, 10, 5), user, props: new() { ["productId"] = "p" }),
            Evt("order_created", At(Day1, 10, 10), user, props: new() { ["orderId"] = "o" }),
            Evt("order_paid", At(Day1.AddDays(1), 9), user, props: new() { ["orderId"] = "o" }));
        await db.SaveChangesAsync();

        var rows = await NewAggregator(db).AggregateRangeAsync(Day1, Day1.AddDays(1));

        Assert.Equal(1, rows);
        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(Day1, stat.Date);
        Assert.Equal(AnalyticsClient.Web, stat.Client);
        Assert.Equal("organic", stat.Channel);
        Assert.Equal(1, stat.ViewProductUv);
        Assert.Equal(1, stat.CheckoutIntentUv);
        Assert.Equal(1, stat.OrderCreatedUv);
        Assert.Equal(1, stat.OrderPaidUv);
    }

    [Fact]
    public async Task Steps_beyond_the_seven_day_window_do_not_count()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 10), user),
            Evt("checkout_intent", At(Day1, 10, 5), user),
            Evt("order_created", At(Day1, 10, 10), user),
            Evt("order_paid", At(Day1.AddDays(8), 9), user));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1);

        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(1, stat.OrderCreatedUv);
        Assert.Equal(0, stat.OrderPaidUv);
    }

    [Fact]
    public async Task A_followup_belongs_to_the_most_recent_open_instance()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), user),
            Evt("page_view_product", At(Day1.AddDays(1), 9), user),
            Evt("checkout_intent", At(Day1.AddDays(1), 10), user));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1.AddDays(1));

        Assert.Equal(2, await db.FunnelDailyStats.CountAsync());
        var day1 = await db.FunnelDailyStats.SingleAsync(s => s.Date == Day1);
        Assert.Equal(1, day1.ViewProductUv);
        Assert.Equal(0, day1.CheckoutIntentUv);
        var day2 = await db.FunnelDailyStats.SingleAsync(s => s.Date == Day1.AddDays(1));
        Assert.Equal(1, day2.ViewProductUv);
        Assert.Equal(1, day2.CheckoutIntentUv);
    }

    [Fact]
    public async Task Session_events_unify_behind_the_logged_in_user()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        var session = Guid.CreateVersion7();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), visitorKey: "anon-v", sessionId: session),
            Evt("checkout_intent", At(Day1, 10), userId: user, visitorKey: "anon-v", sessionId: session));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1);

        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(1, stat.ViewProductUv);
        Assert.Equal(1, stat.CheckoutIntentUv);
    }

    [Fact]
    public async Task Rows_split_by_client_and_channel_of_the_first_step()
    {
        var db = CreateDb();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), visitorKey: "v1", client: AnalyticsClient.Web, channel: "organic"),
            Evt("page_view_product", At(Day1, 11), visitorKey: "v2", client: AnalyticsClient.Miniapp, channel: "promo"));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1);

        Assert.Equal(2, await db.FunnelDailyStats.CountAsync());
        var web = await db.FunnelDailyStats.SingleAsync(s => s.Client == AnalyticsClient.Web);
        Assert.Equal("organic", web.Channel);
        Assert.Equal(1, web.ViewProductUv);
        var miniapp = await db.FunnelDailyStats.SingleAsync(s => s.Client == AnalyticsClient.Miniapp);
        Assert.Equal("promo", miniapp.Channel);
        Assert.Equal(1, miniapp.ViewProductUv);
    }

    [Fact]
    public async Task Distinct_visitors_accumulate_into_uv()
    {
        var db = CreateDb();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), visitorKey: "v1"),
            Evt("page_view_product", At(Day1, 10), visitorKey: "v2"),
            Evt("page_view_product", At(Day1, 11), visitorKey: "v1"));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1);

        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(2, stat.ViewProductUv);
    }

    [Fact]
    public async Task Rerunning_the_window_overwrites_instead_of_duplicating()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), user),
            Evt("checkout_intent", At(Day1, 10), user));
        await db.SaveChangesAsync();
        var aggregator = NewAggregator(db);

        await aggregator.AggregateRangeAsync(Day1, Day1);
        await aggregator.AggregateRangeAsync(Day1, Day1);

        Assert.Equal(1, await db.FunnelDailyStats.CountAsync());
        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(1, stat.ViewProductUv);
        Assert.Equal(1, stat.CheckoutIntentUv);
    }

    [Fact]
    public async Task Steps_out_of_funnel_order_are_skipped()
    {
        var db = CreateDb();
        var user = Guid.CreateVersion7();
        // No intent: a paid event alone never opens or advances an instance.
        db.AnalyticsEvents.AddRange(
            Evt("page_view_product", At(Day1, 9), user),
            Evt("order_paid", At(Day1, 10), user));
        await db.SaveChangesAsync();

        await NewAggregator(db).AggregateRangeAsync(Day1, Day1);

        var stat = await db.FunnelDailyStats.SingleAsync();
        Assert.Equal(1, stat.ViewProductUv);
        Assert.Equal(0, stat.OrderPaidUv);
    }
}
