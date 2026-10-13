namespace Doggo.PriceCrawler;

public sealed class PriceCrawlerRetryOptions
{
    public int MaxAttempts { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 5;
    public int Factor { get; set; } = 2;
    public int CapSeconds { get; set; } = 1800;
    public double JitterRatio { get; set; } = 0.25;
}

public sealed class PriceCrawlerCircuitOptions
{
    public int FailureThreshold { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 60;
}

public sealed class PriceCrawlerRobotsOptions
{
    public bool Enabled { get; set; } = true;
    public int CacheHours { get; set; } = 24;

    /// <summary>When robots.txt cannot be fetched: true = treat as allowed, false = skip the site.</summary>
    public bool FailOpen { get; set; } = true;
}

public sealed class PriceCrawlerNightWindowOptions
{
    public bool Enabled { get; set; }

    /// <summary>"HH:mm" UTC window start; fetches pause inside the window.</summary>
    public string Start { get; set; } = "02:00";
    public string End { get; set; } = "06:00";
}

public sealed class PriceCrawlerDiscoverOptions
{
    public int IntervalHours { get; set; } = 24;

    /// <summary>Preferred daily run time ("HH:mm" UTC); the scheduler runs discovery when both due.</summary>
    public string RunAtUtc { get; set; } = "04:00";
}

public sealed class PriceCrawlerSiteOptions
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between fetch runs for this site (default 60).</summary>
    public int IntervalMinutes { get; set; } = 60;
    public bool RobotsEnabled { get; set; } = true;

    /// <summary>Overrides PriceCrawler:MaxItemsPerRun when set.</summary>
    public int? MaxItemsPerRun { get; set; }

    /// <summary>Game filter for discovery; empty = all games (design §10.10: full by game + whitelist).</summary>
    public List<string> Games { get; set; } = [];

    /// <summary>Market hash name whitelist for discovery; empty = all items.</summary>
    public List<string> MarketHashNames { get; set; } = [];

    /// <summary>Selector set for config-driven HTML sites; ignored when a coded adapter is registered.</summary>
    public Html.HtmlSiteAdapterOptions Html { get; set; } = new();
}

/// <summary>Configuration bound from the "PriceCrawler" section (see design §9).</summary>
public sealed class PriceCrawlerOptions
{
    public const string SectionName = "PriceCrawler";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker re-checks whether sites are due.</summary>
    public int ScanIntervalSeconds { get; set; } = 300;
    public int GlobalConcurrentFetches { get; set; } = 8;
    public int FetchTimeoutSeconds { get; set; } = 60;
    public int MaxItemsPerRun { get; set; } = 200;

    public PriceCrawlerRetryOptions Retry { get; set; } = new();
    public PriceCrawlerCircuitOptions CircuitBreaker { get; set; } = new();
    public PriceCrawlerRobotsOptions Robots { get; set; } = new();

    /// <summary>Declared, honest user agent; the crawler does not masquerade as a browser (§5.4).</summary>
    public string UserAgent { get; set; } = "DoggoPriceBot/1.0 (+price-crawler)";

    public PriceCrawlerNightWindowOptions NightWindowUtc { get; set; } = new();
    public PriceCrawlerDiscoverOptions Discover { get; set; } = new();

    /// <summary>Dedup window: one series point per (site, external id, window).</summary>
    public int DedupWindowMinutes { get; set; } = 60;

    /// <summary>Points deviating more than this percent from the rolling median are flagged suspect.</summary>
    public decimal SuspectThresholdPct { get; set; } = 50;
    public int RollingMedianPoints { get; set; } = 20;
    public long MaxPriceCents { get; set; } = 10_000_000_000;

    /// <summary>Raw capture retention; the series is kept permanently (design §10.8 default).</summary>
    public int RawRetentionDays { get; set; } = 90;

    public List<PriceCrawlerSiteOptions> Sites { get; set; } = [];
}
