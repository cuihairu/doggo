using Doggo.Domain.Common;

namespace Doggo.Domain.Price;

public enum PriceMapStatus
{
    Unconfirmed = 0,
    Confirmed = 1,
    Rejected = 2
}

public enum PriceMapSource
{
    Auto = 0,
    Manual = 1
}

/// <summary>
/// Maps an external site catalog entry to an on-site MarketItem. Discovery creates rows
/// as Unconfirmed; only Confirmed rows feed the cleaned price series (manual gate,
/// see docs/design/price-crawler-design.md §6).
/// </summary>
public class PriceCrawlItemMap : Entity
{
    public string SiteKey { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ExternalTitle { get; set; } = string.Empty;
    public string ExternalUrl { get; set; } = string.Empty;
    public Guid? MarketItemId { get; set; }
    public PriceMapStatus MapStatus { get; set; }
    public PriceMapSource MatchSource { get; set; }
}

/// <summary>Append-only raw capture record, kept for audit and re-cleaning.</summary>
public class PriceCrawlRaw : Entity
{
    public string SiteKey { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public long? PriceCents { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string RawPriceText { get; set; } = string.Empty;
    public string PageFingerprint { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public string AdapterVersion { get; set; } = string.Empty;
}

/// <summary>
/// Cleaned price point: currency-normalized, window-deduplicated, CNY-converted, with a
/// suspect flag for points deviating from the rolling median. Only Confirmed mappings land here.
/// </summary>
public class PriceCrawlSeries : Entity
{
    public Guid MapId { get; set; }
    public Guid MarketItemId { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public long PriceCents { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public long? ConvertedCents { get; set; }
    public decimal? ExchangeRate { get; set; }
    public DateTime ObservedAtUtc { get; set; }
    public string WindowKey { get; set; } = string.Empty;
    public bool IsSuspect { get; set; }
}

/// <summary>
/// Per-site scheduler bookkeeping: last run/discover times, consecutive failures and
/// circuit-open deadline. The design's failure counters live in their own table (§5.2).
/// </summary>
public class PriceCrawlSiteState : Entity
{
    public string SiteKey { get; set; } = string.Empty;
    public DateTime? LastRunAtUtc { get; set; }
    public DateTime? LastDiscoverAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? CircuitOpenUntilUtc { get; set; }

    /// <summary>Crawl-delay last advertised by robots.txt; widens the run interval when stricter.</summary>
    public int RobotsCrawlDelaySeconds { get; set; }

    public string LastError { get; set; } = string.Empty;
}
