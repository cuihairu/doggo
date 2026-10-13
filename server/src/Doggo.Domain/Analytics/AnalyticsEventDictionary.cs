namespace Doggo.Domain.Analytics;

/// <summary>
/// The ingest gate (funnel design §4.4): events outside the dictionary are rejected, and
/// properties are filtered to the per-event whitelist so nothing resembling PII lands in
/// PropertiesJson. New events extend the dictionary without schema changes; property
/// changes bump SchemaVersion.
/// </summary>
public static class AnalyticsEventDictionary
{
    public const int CurrentSchemaVersion = 1;

    private static readonly Dictionary<string, string[]> Whitelists = new()
    {
        ["page_view_product"] = ["productId", "source"],
        ["checkout_intent"] = ["productId", "planId", "optionId", "quantity"],
        ["order_created"] = ["orderId", "productId", "entry", "serviceMode", "fulfillmentType", "totalPriceCents", "payType"],
        ["order_paid"] = ["orderId", "payType", "amountCents"]
    };

    public static IReadOnlyCollection<string> KnownEventNames => Whitelists.Keys;

    public static bool IsKnown(string eventName, int schemaVersion) =>
        schemaVersion == CurrentSchemaVersion && Whitelists.ContainsKey(eventName);

    public static bool TryGetWhitelist(string eventName, out IReadOnlyCollection<string> keys)
    {
        if (Whitelists.TryGetValue(eventName, out var whitelist))
        {
            keys = whitelist;
            return true;
        }

        keys = [];
        return false;
    }
}
