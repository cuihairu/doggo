using System.Security.Claims;
using System.Text.Json;
using Doggo.Domain.Analytics;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Analytics;

/// <summary>
/// Single write entry for analytics events (funnel design §6). The two Enqueue methods add
/// event rows to the caller's SaveChanges batch so order and payment events are exactly as
/// durable as the business rows; RecordAsync persists standalone ingest rows.
/// </summary>
public interface IAnalyticsRecorder
{
    void EnqueueOrderCreated(UserOrder order);

    void EnqueueOrderPaid(UserOrder order, PaymentRecord payment);

    Task RecordAsync(AnalyticsEvent e, CancellationToken ct = default);
}

public sealed partial class AnalyticsRecorder(
    DoggoDbContext db,
    IHttpContextAccessor accessor,
    ILogger<AnalyticsRecorder> logger) : IAnalyticsRecorder
{
    private const string SessionHeader = "X-Doggo-Session";
    private const string ClientHeader = "X-Doggo-Client";
    private const string ChannelHeader = "X-Doggo-Channel";

    public void EnqueueOrderCreated(UserOrder order)
    {
        // CreatedAtUtc is stamped by the audit pipeline at save time; "now" is the same instant.
        var e = NewEvent("order_created", order.CreatedAtUtc == default ? DateTime.UtcNow : order.CreatedAtUtc, order.UserId);
        e.PropertiesJson = ToJson("order_created", new Dictionary<string, object?>
        {
            ["orderId"] = order.Id,
            ["productId"] = order.ProductId,
            ["entry"] = order.Entry.ToString(),
            ["serviceMode"] = order.ServiceMode.ToString(),
            ["fulfillmentType"] = order.FulfillmentType.ToString(),
            ["totalPriceCents"] = order.TotalPriceCents,
            ["payType"] = order.PayType.ToString()
        });
        db.AnalyticsEvents.Add(e);
    }

    public void EnqueueOrderPaid(UserOrder order, PaymentRecord payment)
    {
        // Callback requests have no client context (§6.2): session/channel stay empty, the
        // aggregation joins back to the order_created event via orderId when ever needed.
        var e = new AnalyticsEvent
        {
            EventName = "order_paid",
            OccurredAtUtc = order.PaidAtUtc ?? DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            SessionId = Guid.Empty,
            UserId = order.UserId,
            VisitorKey = string.Empty,
            Client = AnalyticsClient.Web,
            Channel = string.Empty,
            IdempotencyKey = $"order-paid:{payment.IdempotencyKey}",
            SchemaVersion = AnalyticsEventDictionary.CurrentSchemaVersion
        };
        e.PropertiesJson = ToJson("order_paid", new Dictionary<string, object?>
        {
            ["orderId"] = order.Id,
            ["payType"] = order.PayType.ToString(),
            ["amountCents"] = payment.AmountCents
        });
        db.AnalyticsEvents.Add(e);
        LogOrderPaid(logger, order.OrderNo);
    }

    public async Task RecordAsync(AnalyticsEvent e, CancellationToken ct = default)
    {
        if (e.ReceivedAtUtc == default)
        {
            e.ReceivedAtUtc = DateTime.UtcNow;
        }

        db.AnalyticsEvents.Add(e);
        await db.SaveChangesAsync(ct);
    }

    private AnalyticsEvent NewEvent(string eventName, DateTime occurredAtUtc, Guid? userId)
    {
        var context = accessor.HttpContext;
        Guid.TryParse(context?.Request.Headers[SessionHeader], out var sessionId);

        return new AnalyticsEvent
        {
            EventName = eventName,
            OccurredAtUtc = occurredAtUtc,
            ReceivedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            UserId = userId ?? ReadUserId(context),
            VisitorKey = context is null
                ? string.Empty
                : VisitorKeys.Compute(
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString()),
            Client = ParseClient(context?.Request.Headers[ClientHeader]),
            Channel = NormalizeChannel(context?.Request.Headers[ChannelHeader]),
            IdempotencyKey = $"{eventName}:{Guid.NewGuid():N}",
            SchemaVersion = AnalyticsEventDictionary.CurrentSchemaVersion
        };
    }

    private static Guid? ReadUserId(HttpContext? context)
    {
        var sub = context?.User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private static AnalyticsClient ParseClient(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "miniapp" => AnalyticsClient.Miniapp,
        "mobile" => AnalyticsClient.Mobile,
        "admin" => AnalyticsClient.Admin,
        _ => AnalyticsClient.Web
    };

    private static string NormalizeChannel(string? value)
    {
        var channel = value?.Trim();
        return string.IsNullOrEmpty(channel) ? "organic" : channel;
    }

    private static string ToJson(string eventName, Dictionary<string, object?> properties)
    {
        AnalyticsEventDictionary.TryGetWhitelist(eventName, out var whitelist);
        var filtered = properties
            .Where(kv => whitelist.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return JsonSerializer.Serialize(filtered);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "order {OrderNo} paid event recorded")]
    private static partial void LogOrderPaid(ILogger logger, string orderNo);
}
