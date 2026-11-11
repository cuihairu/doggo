using System.Text.Json;
using System.Text.Json.Serialization;
using Doggo.Infrastructure.Orders;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Vapor;

/// <summary>
/// Applies vapor webhook events that settle an order retrieval. The envelope and HMAC scheme
/// match vapor's verified webhook push; the <c>order_retrieval/trade_completed</c> category is
/// a doggo-side convention for the moment vapor's notification service emits the event. The
/// offer id in the payload must match the offer this deployment dispatched for the order, so a
/// foreign or stale callback cannot complete an order it does not own. Malformed payloads and
/// offers that do not match are logged and swallowed (the webhook is already idempotency-claimed,
/// so vapor retries would be deduped anyway); unexpected failures propagate so the endpoint can
/// release the claim and let vapor retry.
/// </summary>
public sealed partial class OrderRetrievalWebhookHandler(
    OrderService orders,
    ILogger<OrderRetrievalWebhookHandler> logger)
{
    public const string Category = "order_retrieval";
    public const string TradeCompletedType = "trade_completed";

    public async Task HandleAsync(VaporWebhookEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await TryHandleAsync(envelope, cancellationToken))
            {
                LogUnknownType(envelope.Id, envelope.Type);
            }
        }
        catch (Exception ex) when (ex is JsonException or OrderNotFoundException or InvalidOperationException)
        {
            LogRejected(envelope.Id, envelope.Type, ex.Message);
        }
    }

    private async Task<bool> TryHandleAsync(VaporWebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Category, Category, StringComparison.OrdinalIgnoreCase)
            || envelope.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            return false;
        }

        if (!string.Equals(envelope.Type, TradeCompletedType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var step = payload.Deserialize<TradeCompletedPayload>(VaporJson.Options)
            ?? throw new JsonException("trade_completed payload was null");

        var completed = await orders.CompleteRetrievalAsync(step.OrderId, step.OfferId, cancellationToken);
        if (!completed)
        {
            // Not Retrieving, or the offer id is not the one this deployment dispatched: the
            // order is not owed a completion from this callback. Log and acknowledge so vapor
            // does not retry a callback that can never apply.
            LogNotCompleted(envelope.Id, step.OrderId, step.OfferId);
        }

        return true;
    }

    private sealed record TradeCompletedPayload(
        [property: JsonPropertyName("order_id")] Guid OrderId,
        [property: JsonPropertyName("offer_id")] string OfferId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor webhook {WebhookId}: unhandled order_retrieval type {Type}")]
    private partial void LogUnknownType(Guid webhookId, string? type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor webhook {WebhookId}: {Type} rejected: {Reason}")]
    private partial void LogRejected(Guid webhookId, string? type, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor webhook {WebhookId}: trade completion for order {OrderId} offer {OfferId} was not applied")]
    private partial void LogNotCompleted(Guid webhookId, Guid orderId, string offerId);
}
