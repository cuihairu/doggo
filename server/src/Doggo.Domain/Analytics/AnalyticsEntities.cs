using Doggo.Domain.Common;

namespace Doggo.Domain.Analytics;

public enum AnalyticsClient
{
    Web = 0,
    Miniapp = 1,
    Mobile = 2,
    Admin = 3
}

/// <summary>
/// One ingested behaviour or server-authoritative event (funnel design §4). Raw facts only:
/// identity backfill happens at aggregation time, never by rewriting rows.
/// </summary>
public class AnalyticsEvent : Entity
{
    public string EventName { get; set; } = string.Empty;

    /// <summary>Client event time (offline catch-up can be days old); server events use business time.</summary>
    public DateTime OccurredAtUtc { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Client-generated; server events use the request context (empty for callbacks).</summary>
    public Guid SessionId { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Server-computed from (ip, user agent), same algorithm as UserVisitLog.</summary>
    public string VisitorKey { get; set; } = string.Empty;

    public AnalyticsClient Client { get; set; }

    public string Channel { get; set; } = "organic";

    public string PropertiesJson { get; set; } = "{}";

    /// <summary>Client-generated, stable across retries; the unique index drops replays.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    /// <summary>Page path with any query string stripped (§10.1).</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Daily funnel UVs keyed by (Date, Client, Channel), attributed to the first step's day.
/// Rewritten wholesale by the rolling recompute; the funnel page never scans events.
/// </summary>
public class FunnelDailyStat : Entity
{
    public DateOnly Date { get; set; }

    public AnalyticsClient Client { get; set; }

    public string Channel { get; set; } = string.Empty;

    public long ViewProductUv { get; set; }

    public long CheckoutIntentUv { get; set; }

    public long OrderCreatedUv { get; set; }

    public long OrderPaidUv { get; set; }
}
