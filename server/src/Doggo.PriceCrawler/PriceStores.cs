using Doggo.Domain.Price;

namespace Doggo.PriceCrawler;

/// <summary>Persistence contracts implemented in Doggo.Infrastructure (module keeps no EF dependency).</summary>
public interface IPriceCrawlMapStore
{
    Task<PriceCrawlItemMap?> FindAsync(string siteKey, string externalId, CancellationToken cancellationToken);

    Task<PriceCrawlItemMap> GetOrAddAsync(string siteKey, string externalId, string title, string detailUrl, CancellationToken cancellationToken);

    Task<PriceCrawlItemMap?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Persists status changes made on a fetched map (confirm/reject).</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceCrawlItemMap>> ListAsync(PriceMapStatus? status, int skip, int take, CancellationToken cancellationToken);

    /// <summary>All known mappings for one site (scheduler filters Rejected and caps per run).</summary>
    Task<IReadOnlyList<PriceCrawlItemMap>> ListBySiteAsync(string siteKey, int take, CancellationToken cancellationToken);
}

public interface IPriceCrawlRawStore
{
    Task AddAsync(PriceCrawlRaw raw, CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}

public interface IPriceCrawlSeriesStore
{
    /// <summary>
    /// Window dedup (design §6): one point per (map, window); a later observation replaces the
    /// earlier one ("keep last"). Returns true when a row was written.
    /// </summary>
    Task<bool> UpsertWindowedAsync(PriceCrawlSeries point, CancellationToken cancellationToken);

    /// <summary>Last <paramref name="take"/> unified compare values (ConvertedCents ?? PriceCents) for the rolling median.</summary>
    Task<IReadOnlyList<long>> RecentCompareCentsAsync(Guid mapId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<PriceCrawlSeries>> QueryByMarketItemAsync(Guid marketItemId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken);
}

public interface IPriceCrawlSiteStateStore
{
    Task<PriceCrawlSiteState> GetOrAddAsync(string siteKey, CancellationToken cancellationToken);

    Task SaveAsync(PriceCrawlSiteState state, CancellationToken cancellationToken);
}

public interface IPriceCrawlQueries
{
    Task<IReadOnlyList<PriceSpreadRow>> SpreadReportAsync(int recentDays, CancellationToken cancellationToken);
}

/// <summary>Reads ExchangeRate rows (1 unit of <c>currencyCode</c> = Rate CNY); missing rate → null.</summary>
public interface IExchangeRateReader
{
    Task<decimal?> GetRateToCnyAsync(string currencyCode, CancellationToken cancellationToken);
}

public sealed record PriceSpreadRow(
    Guid MarketItemId,
    int SiteCount,
    long MinCents,
    long MaxCents,
    long SpreadCents,
    decimal SpreadPct);
