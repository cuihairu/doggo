using Doggo.Domain.Analytics;

namespace Doggo.Infrastructure.Analytics;

/// <summary>
/// Resolves the X-Doggo-Client header to an AnalyticsClient (funnel design §6, §9.3).
/// Event ingest, the server-authoritative recorder and the visit-log middleware all read
/// through this so every writer applies one parsing rule.
/// </summary>
public static class AnalyticsClients
{
    public const string ClientHeader = "X-Doggo-Client";

    public static AnalyticsClient ParseClient(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "miniapp" => AnalyticsClient.Miniapp,
        "mobile" => AnalyticsClient.Mobile,
        "admin" => AnalyticsClient.Admin,
        _ => AnalyticsClient.Web
    };
}
