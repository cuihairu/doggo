using System.Text.Json;
using Doggo.Domain.Analytics;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

/// <summary>Server-authoritative events share the business SaveChanges batch (design §6).</summary>
public class AnalyticsRecorderTests
{
    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"analytics-recorder-{Guid.NewGuid():N}")
            .Options);

    private static UserOrder NewOrder() => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = Guid.CreateVersion7(),
        OrderNo = "D20261013001",
        ProductId = Guid.CreateVersion7(),
        Entry = OrderEntry.Case,
        TotalPriceCents = 9900,
        TotalCostCents = 9900,
        Status = OrderStatus.PendingPayment,
        ServiceMode = ServiceMode.CloudOpen,
        FulfillmentType = FulfillmentType.Spot,
        PayType = PaymentProvider.None
    };

    private static AnalyticsRecorder NewRecorder(DoggoDbContext db, HttpContext? context = null) =>
        new(db, new HttpContextAccessor { HttpContext = context }, NullLogger<AnalyticsRecorder>.Instance);

    [Fact]
    public async Task Enqueue_order_created_flows_out_with_the_same_save_as_the_order()
    {
        var db = CreateDb();
        var recorder = NewRecorder(db);
        var order = NewOrder();

        db.UserOrders.Add(order);
        recorder.EnqueueOrderCreated(order);
        await db.SaveChangesAsync();

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "order_created");
        Assert.Equal(order.UserId, evt.UserId);
        Assert.Equal(1, evt.SchemaVersion);

        var props = JsonDocument.Parse(evt.PropertiesJson).RootElement;
        Assert.Equal(order.Id.ToString(), props.GetProperty("orderId").GetString());
        Assert.Equal(order.ProductId.ToString(), props.GetProperty("productId").GetString());
        Assert.Equal(order.TotalPriceCents, props.GetProperty("totalPriceCents").GetInt64());
        Assert.Equal(order.Entry.ToString(), props.GetProperty("entry").GetString());
    }

    [Fact]
    public async Task Enqueue_order_paid_carries_amount_and_is_sessionless()
    {
        var db = CreateDb();
        var recorder = NewRecorder(db);
        var order = NewOrder();
        order.PaidAtUtc = new DateTime(2026, 10, 13, 8, 30, 0, DateTimeKind.Utc);
        var payment = new PaymentRecord
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            AmountCents = 9900,
            Status = PaymentStatus.Success
        };

        db.UserOrders.Add(order);
        recorder.EnqueueOrderPaid(order, payment);
        await db.SaveChangesAsync();

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "order_paid");
        Assert.Equal(order.UserId, evt.UserId);
        Assert.Equal(order.PaidAtUtc, evt.OccurredAtUtc);
        Assert.Equal(Guid.Empty, evt.SessionId);
        Assert.Equal(string.Empty, evt.Channel);

        var props = JsonDocument.Parse(evt.PropertiesJson).RootElement;
        Assert.Equal(order.Id.ToString(), props.GetProperty("orderId").GetString());
        Assert.Equal(9900, props.GetProperty("amountCents").GetInt64());
    }

    [Fact]
    public async Task Record_async_saves_immediately_and_stamps_received_at()
    {
        var db = CreateDb();
        var recorder = NewRecorder(db);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await recorder.RecordAsync(new AnalyticsEvent
        {
            EventName = "page_view_product",
            OccurredAtUtc = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc),
            SessionId = Guid.CreateVersion7(),
            VisitorKey = "abc",
            IdempotencyKey = "k-1"
        });

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.IdempotencyKey == "k-1");
        Assert.True(evt.ReceivedAtUtc >= before);
        Assert.Equal("page_view_product", evt.EventName);
        Assert.Null(evt.UserId);
    }

    [Fact]
    public async Task Request_context_supplies_session_client_and_channel()
    {
        var db = CreateDb();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Doggo-Session"] = "01993c1e-8a4b-7c30-9d2f-1e5c6b7a8f01";
        context.Request.Headers["X-Doggo-Client"] = "miniapp";
        context.Request.Headers["X-Doggo-Channel"] = "promo";
        var recorder = NewRecorder(db, context);

        recorder.EnqueueOrderCreated(NewOrder());
        await db.SaveChangesAsync();

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "order_created");
        Assert.Equal(Guid.Parse("01993c1e-8a4b-7c30-9d2f-1e5c6b7a8f01"), evt.SessionId);
        Assert.Equal(AnalyticsClient.Miniapp, evt.Client);
        Assert.Equal("promo", evt.Channel);
        Assert.Equal(32, evt.VisitorKey.Length);
    }

    [Fact]
    public async Task Missing_or_malformed_context_falls_back_to_defaults()
    {
        var db = CreateDb();

        var recorder = NewRecorder(db);
        recorder.EnqueueOrderCreated(NewOrder());
        await db.SaveChangesAsync();

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "order_created");
        Assert.Equal(Guid.Empty, evt.SessionId);
        Assert.Equal(AnalyticsClient.Web, evt.Client);
        Assert.Equal("organic", evt.Channel);
        Assert.Equal(string.Empty, evt.VisitorKey);

        var withBrokenSession = new DefaultHttpContext();
        withBrokenSession.Request.Headers["X-Doggo-Session"] = "not-a-guid";
        var recorder2 = NewRecorder(db, withBrokenSession);
        recorder2.EnqueueOrderCreated(NewOrder());
        await db.SaveChangesAsync();

        Assert.Equal(Guid.Empty, (await db.AnalyticsEvents.LastAsync(e => e.EventName == "order_created")).SessionId);
    }
}
