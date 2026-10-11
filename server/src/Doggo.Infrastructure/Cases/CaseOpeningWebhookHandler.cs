using System.Text.Json;
using System.Text.Json.Serialization;
using Doggo.Domain.Cases;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Vapor;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Cases;

/// <summary>
/// Applies vapor webhook events for the CaseOpening flow onto batch seats. The payload
/// shape is provisional until vapor 联调 lands; unknown or malformed payloads are logged
/// and swallowed (the webhook is already idempotency-claimed, so vapor retries would be
/// deduped anyway) instead of failing the webhook call.
///
/// Handled types:
/// - opening_result: the drop — payload {batch_id, seat_no, history_id, item{...}} creates
///   the prize and moves the seat to Opened.
/// - stock_fetch_started / stock_ready / trade_started / trade_completed: advance the seat
///   along FetchingStock → StockReady → Trading → Completed via {batch_id, seat_no}.
/// </summary>
public sealed partial class CaseOpeningWebhookHandler(
    GroupBuyingService batches,
    ILogger<CaseOpeningWebhookHandler> logger)
{
    public const string Category = "case_opening";

    public async Task HandleAsync(VaporWebhookEnvelope envelope, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await TryHandleAsync(envelope, cancellationToken))
            {
                LogUnknownType(envelope.Id, envelope.Type);
            }
        }
        catch (Exception ex) when (ex is JsonException or CaseValidationException or BatchNotFoundException or SeatNotFoundException or InvalidOperationException)
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

        switch (envelope.Type.ToLowerInvariant())
        {
            case "opening_result":
                var result = payload.Deserialize<OpeningResultPayload>(VaporJson.Options)
                    ?? throw new JsonException("opening_result payload was null");
                await batches.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(
                    result.BatchId,
                    result.SeatNo,
                    ToItem(result.Item),
                    result.HistoryId,
                    payload.GetRawText()), cancellationToken);
                return true;

            case "stock_fetch_started":
                return await AdvanceAsync(payload, SeatStatus.FetchingStock, cancellationToken);

            case "stock_ready":
                return await AdvanceAsync(payload, SeatStatus.StockReady, cancellationToken);

            case "trade_started":
                return await AdvanceAsync(payload, SeatStatus.Trading, cancellationToken);

            case "trade_completed":
                return await AdvanceAsync(payload, SeatStatus.Completed, cancellationToken);

            default:
                return false;
        }
    }

    private async Task<bool> AdvanceAsync(JsonElement payload, SeatStatus target, CancellationToken cancellationToken)
    {
        var step = payload.Deserialize<SeatRefPayload>(VaporJson.Options)
            ?? throw new JsonException("seat payload was null");
        await batches.AdvanceSeatAsync(step.BatchId, step.SeatNo, target, cancellationToken);
        return true;
    }

    private static OpeningResultItem ToItem(OpeningItemPayload? item) =>
        item is null
            ? throw new JsonException("opening_result payload has no item")
            : new OpeningResultItem(
                item.AssetId,
                item.ClassId ?? string.Empty,
                item.InstanceId ?? string.Empty,
                item.MarketHashName ?? string.Empty,
                item.Quality is > 0 ? (CaseQuality)item.Quality.Value : CaseQuality.Consumer,
                item.Exterior ?? string.Empty,
                item.FloatValue,
                item.Rarity ?? string.Empty,
                item.IconUrl ?? string.Empty,
                item.InspectLink,
                item.PriceCents ?? 0,
                item.TradableAtUtc);

    private sealed record SeatRefPayload(
        [property: JsonPropertyName("batch_id")] Guid BatchId,
        [property: JsonPropertyName("seat_no")] int SeatNo);

    private sealed record OpeningItemPayload(
        [property: JsonPropertyName("asset_id")] string AssetId,
        [property: JsonPropertyName("class_id")] string? ClassId,
        [property: JsonPropertyName("instance_id")] string? InstanceId,
        [property: JsonPropertyName("market_hash_name")] string? MarketHashName,
        [property: JsonPropertyName("quality")] int? Quality,
        [property: JsonPropertyName("exterior")] string? Exterior,
        [property: JsonPropertyName("float_value")] double? FloatValue,
        [property: JsonPropertyName("rarity")] string? Rarity,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("inspect_link")] string? InspectLink,
        [property: JsonPropertyName("price_cents")] long? PriceCents,
        [property: JsonPropertyName("tradable_at")] DateTime? TradableAtUtc);

    private sealed record OpeningResultPayload(
        [property: JsonPropertyName("batch_id")] Guid BatchId,
        [property: JsonPropertyName("seat_no")] int SeatNo,
        [property: JsonPropertyName("history_id")] long? HistoryId,
        [property: JsonPropertyName("item")] OpeningItemPayload? Item);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor webhook {WebhookId}: unhandled case_opening type {Type}")]
    private partial void LogUnknownType(Guid webhookId, string? type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "vapor webhook {WebhookId}: {Type} rejected: {Reason}")]
    private partial void LogRejected(Guid webhookId, string? type, string reason);
}
