namespace Doggo.PriceCrawler;

public sealed record SiteRateLimit(int MinIntervalSeconds, int MaxConcurrent, string? AllowedWindowUtc);

public sealed record DiscoveryQuery(string? MarketHashName, string? ClassId, string? Keyword);

public sealed record ExternalItemRef(string ExternalId, string Title, string DetailUrl);

/// <summary>
/// One observed price. <see cref="PriceCents"/> is in the site currency's minor unit;
/// the adapter never converts currency. <see cref="RawPriceText"/> and
/// <see cref="PageFingerprint"/> are optional provenance for the raw capture table.
/// </summary>
public sealed record ExternalPriceFetch(
    string ExternalId,
    long PriceCents,
    string CurrencyCode,
    DateTimeOffset ObservedAtUtc,
    string? RawPriceText = null,
    string? PageFingerprint = null);

/// <summary>
/// One site adapter: discovers external catalog entries and fetches their current prices.
/// Failures return empty results rather than throwing; only transport errors propagate so
/// the scheduler can back off. See docs/design/price-crawler-design.md §3.
/// </summary>
public interface ISiteAdapter
{
    /// <summary>Stable site identifier (e.g. "example-market"); must match a configured site key.</summary>
    string SiteKey { get; }

    /// <summary>Adapter version (SemVer-ish) stored on captured rows to trace parsing changes.</summary>
    string AdapterVersion { get; }

    /// <summary>Entry URL the scheduler checks against robots.txt before running this adapter.</summary>
    string RobotsUrl { get; }

    IReadOnlyList<string> SupportedCurrencyCodes { get; }

    SiteRateLimit SuggestedRateLimit { get; }

    Task<IReadOnlyList<ExternalItemRef>> DiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalPriceFetch>> FetchPricesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken);
}

/// <summary>Resolves the adapter for a configured site key.</summary>
public sealed class SiteAdapterRegistry(IEnumerable<ISiteAdapter> adapters)
{
    private readonly Dictionary<string, ISiteAdapter> _bySite =
        adapters.ToDictionary(a => a.SiteKey, StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string siteKey, out ISiteAdapter adapter) =>
        _bySite.TryGetValue(siteKey, out adapter!);
}
