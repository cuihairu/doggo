using Doggo.Domain.Price;
using Doggo.PriceCrawler;

namespace Doggo.PriceCrawler.Tests;

/// <summary>List-backed stores for module tests (no EF dependency needed here).</summary>
public sealed class FakePriceCrawlStores
{
    public List<PriceCrawlItemMap> Maps { get; } = [];
    public List<PriceCrawlRaw> Raws { get; } = [];
    public List<PriceCrawlSeries> Series { get; } = [];
    public List<PriceCrawlSiteState> SiteStates { get; } = [];
    public Dictionary<string, decimal> Rates { get; } = new();

    public IPriceCrawlMapStore MapStore { get; }
    public IPriceCrawlRawStore RawStore { get; }
    public IPriceCrawlSeriesStore SeriesStore { get; }
    public IPriceCrawlSiteStateStore SiteStateStore { get; }
    public IPriceCrawlQueries Queries { get; }
    public IExchangeRateReader ExchangeRates { get; }

    public FakePriceCrawlStores()
    {
        MapStore = new FakeMapStore(this);
        RawStore = new FakeRawStore(this);
        SeriesStore = new FakeSeriesStore(this);
        SiteStateStore = new FakeSiteStateStore(this);
        Queries = new FakeQueries(this);
        ExchangeRates = new FakeRates(this);
    }

    private sealed class FakeMapStore(FakePriceCrawlStores owner) : IPriceCrawlMapStore
    {
        public Task<PriceCrawlItemMap?> FindAsync(string siteKey, string externalId, CancellationToken cancellationToken) =>
            Task.FromResult(owner.Maps.FirstOrDefault(m => m.SiteKey == siteKey && m.ExternalId == externalId));

        public Task<PriceCrawlItemMap> GetOrAddAsync(string siteKey, string externalId, string title, string detailUrl, CancellationToken cancellationToken)
        {
            var existing = owner.Maps.FirstOrDefault(m => m.SiteKey == siteKey && m.ExternalId == externalId);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var map = new PriceCrawlItemMap
            {
                Id = Guid.CreateVersion7(),
                SiteKey = siteKey,
                ExternalId = externalId,
                ExternalTitle = title,
                ExternalUrl = detailUrl
            };
            owner.Maps.Add(map);
            return Task.FromResult(map);
        }

        public Task<PriceCrawlItemMap?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(owner.Maps.FirstOrDefault(m => m.Id == id));

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<PriceCrawlItemMap>> ListAsync(PriceMapStatus? status, int skip, int take, CancellationToken cancellationToken)
        {
            IEnumerable<PriceCrawlItemMap> query = owner.Maps;
            if (status is not null)
            {
                query = query.Where(m => m.MapStatus == status);
            }

            return Task.FromResult(query.Skip(skip).Take(take).ToList() as IReadOnlyList<PriceCrawlItemMap>);
        }

        public Task<IReadOnlyList<PriceCrawlItemMap>> ListBySiteAsync(string siteKey, int take, CancellationToken cancellationToken) =>
            Task.FromResult(owner.Maps.Where(m => m.SiteKey == siteKey).Take(take).ToList() as IReadOnlyList<PriceCrawlItemMap>);
    }

    private sealed class FakeRawStore(FakePriceCrawlStores owner) : IPriceCrawlRawStore
    {
        public Task AddAsync(PriceCrawlRaw raw, CancellationToken cancellationToken)
        {
            raw.Id = Guid.CreateVersion7();
            owner.Raws.Add(raw);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            var old = owner.Raws.Where(r => r.FetchedAtUtc < cutoffUtc).ToList();
            foreach (var row in old)
            {
                owner.Raws.Remove(row);
            }

            return Task.FromResult(old.Count);
        }
    }

    private sealed class FakeSeriesStore(FakePriceCrawlStores owner) : IPriceCrawlSeriesStore
    {
        public Task<bool> UpsertWindowedAsync(PriceCrawlSeries point, CancellationToken cancellationToken)
        {
            point.Id = Guid.CreateVersion7();
            var existing = owner.Series.FirstOrDefault(p => p.MapId == point.MapId && p.WindowKey == point.WindowKey);
            if (existing is null)
            {
                owner.Series.Add(point);
                return Task.FromResult(true);
            }

            if (point.ObservedAtUtc < existing.ObservedAtUtc)
            {
                return Task.FromResult(false);
            }

            existing.PriceCents = point.PriceCents;
            existing.ConvertedCents = point.ConvertedCents;
            existing.ExchangeRate = point.ExchangeRate;
            existing.ObservedAtUtc = point.ObservedAtUtc;
            existing.IsSuspect = point.IsSuspect;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<long>> RecentCompareCentsAsync(Guid mapId, int take, CancellationToken cancellationToken) =>
            Task.FromResult(owner.Series
                .Where(p => p.MapId == mapId)
                .OrderByDescending(p => p.ObservedAtUtc)
                .Take(take)
                .Select(p => p.ConvertedCents ?? p.PriceCents)
                .ToList() as IReadOnlyList<long>);

        public Task<IReadOnlyList<PriceCrawlSeries>> QueryByMarketItemAsync(Guid marketItemId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
        {
            IEnumerable<PriceCrawlSeries> query = owner.Series.Where(p => p.MarketItemId == marketItemId);
            if (fromUtc is not null)
            {
                query = query.Where(p => p.ObservedAtUtc >= fromUtc);
            }

            if (toUtc is not null)
            {
                query = query.Where(p => p.ObservedAtUtc <= toUtc);
            }

            return Task.FromResult(query.OrderBy(p => p.ObservedAtUtc).ToList() as IReadOnlyList<PriceCrawlSeries>);
        }
    }

    private sealed class FakeSiteStateStore(FakePriceCrawlStores owner) : IPriceCrawlSiteStateStore
    {
        public Task<PriceCrawlSiteState> GetOrAddAsync(string siteKey, CancellationToken cancellationToken)
        {
            var state = owner.SiteStates.FirstOrDefault(s => s.SiteKey == siteKey);
            if (state is not null)
            {
                return Task.FromResult(state);
            }

            state = new PriceCrawlSiteState { Id = Guid.CreateVersion7(), SiteKey = siteKey };
            owner.SiteStates.Add(state);
            return Task.FromResult(state);
        }

        public Task SaveAsync(PriceCrawlSiteState state, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeQueries(FakePriceCrawlStores owner) : IPriceCrawlQueries
    {
        public Task<IReadOnlyList<PriceSpreadRow>> SpreadReportAsync(int recentDays, CancellationToken cancellationToken)
        {
            var from = DateTime.UtcNow.AddDays(-Math.Max(recentDays, 1));
            var rows = owner.Series
                .Where(p => !p.IsSuspect && p.ObservedAtUtc >= from && p.ConvertedCents is not null)
                .GroupBy(p => p.MarketItemId)
                .Select(g =>
                {
                    var values = g.Select(p => p.ConvertedCents!.Value).ToArray();
                    var min = values.Min();
                    var max = values.Max();
                    return new PriceSpreadRow(g.Key, g.Select(p => p.SiteKey).Distinct().Count(), min, max, max - min,
                        min == 0 ? 0 : Math.Round((max - min) * 100m / min, 2));
                })
                .ToList();
            return Task.FromResult(rows as IReadOnlyList<PriceSpreadRow>);
        }
    }

    private sealed class FakeRates(FakePriceCrawlStores owner) : IExchangeRateReader
    {
        public Task<decimal?> GetRateToCnyAsync(string currencyCode, CancellationToken cancellationToken)
        {
            var code = currencyCode.Trim().ToUpperInvariant();
            decimal? rate = code == "CNY" ? 1m
                : owner.Rates.TryGetValue(code, out var value) ? value
                : null;
            return Task.FromResult(rate);
        }
    }
}
