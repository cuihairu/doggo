using Doggo.Domain.Price;
using Doggo.Infrastructure.Persistence;
using Doggo.PriceCrawler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.PriceCrawl;

/// <summary>
/// EF implementations of the crawler store contracts (same idioms as MarketStore:
/// application-side GUID v7 keys, change-tracker lookups before database queries).
/// </summary>
public sealed class PriceCrawlMapStore(DoggoDbContext db) : IPriceCrawlMapStore
{
    public async Task<PriceCrawlItemMap?> FindAsync(string siteKey, string externalId, CancellationToken cancellationToken)
    {
        var local = db.PriceCrawlItemMaps.Local.FirstOrDefault(m => m.SiteKey == siteKey && m.ExternalId == externalId);
        if (local is not null)
        {
            return local;
        }

        return await db.PriceCrawlItemMaps.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SiteKey == siteKey && m.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PriceCrawlItemMap> GetOrAddAsync(string siteKey, string externalId, string title, string detailUrl, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(siteKey, externalId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var map = new PriceCrawlItemMap
        {
            Id = Guid.CreateVersion7(),
            SiteKey = siteKey,
            ExternalId = externalId,
            ExternalTitle = title,
            ExternalUrl = detailUrl,
            MapStatus = PriceMapStatus.Unconfirmed,
            MatchSource = PriceMapSource.Auto
        };
        db.PriceCrawlItemMaps.Add(map);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return map;
    }

    public async Task<PriceCrawlItemMap?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.PriceCrawlItemMaps.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<PriceCrawlItemMap>> ListAsync(PriceMapStatus? status, int skip, int take, CancellationToken cancellationToken)
    {
        var query = db.PriceCrawlItemMaps.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(m => m.MapStatus == status);
        }

        return await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PriceCrawlItemMap>> ListBySiteAsync(string siteKey, int take, CancellationToken cancellationToken) =>
        await db.PriceCrawlItemMaps.AsNoTracking()
            .Where(m => m.SiteKey == siteKey)
            .OrderBy(m => m.ExternalId)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

public sealed class PriceCrawlRawStore(DoggoDbContext db, ILogger<PriceCrawlRawStore> logger) : IPriceCrawlRawStore
{
    private const int PurgeBatchSize = 500;

    public async Task AddAsync(PriceCrawlRaw raw, CancellationToken cancellationToken)
    {
        raw.Id = Guid.CreateVersion7();
        db.PriceCrawlRaws.Add(raw);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Batched purge (InMemory provider has no ExecuteDelete; the loop bounds memory).</summary>
    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        var deleted = 0;
        while (true)
        {
            var batch = await db.PriceCrawlRaws
                .Where(r => r.FetchedAtUtc < cutoffUtc)
                .OrderBy(r => r.FetchedAtUtc)
                .Take(PurgeBatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            db.PriceCrawlRaws.RemoveRange(batch);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            deleted += batch.Count;
            if (batch.Count < PurgeBatchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            PriceCrawlLog.RawPurged(logger, deleted, cutoffUtc);
        }

        return deleted;
    }
}

public sealed class PriceCrawlSeriesStore(DoggoDbContext db) : IPriceCrawlSeriesStore
{
    public async Task<bool> UpsertWindowedAsync(PriceCrawlSeries point, CancellationToken cancellationToken)
    {
        point.Id = Guid.CreateVersion7();
        var existing = db.PriceCrawlSeriesPoints.Local.FirstOrDefault(p => p.MapId == point.MapId && p.WindowKey == point.WindowKey);
        existing ??= await db.PriceCrawlSeriesPoints
            .FirstOrDefaultAsync(p => p.MapId == point.MapId && p.WindowKey == point.WindowKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            db.PriceCrawlSeriesPoints.Add(point);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Keep last: a later observation replaces the earlier one in the same window.
        if (point.ObservedAtUtc < existing.ObservedAtUtc)
        {
            return false;
        }

        existing.PriceCents = point.PriceCents;
        existing.CurrencyCode = point.CurrencyCode;
        existing.ConvertedCents = point.ConvertedCents;
        existing.ExchangeRate = point.ExchangeRate;
        existing.ObservedAtUtc = point.ObservedAtUtc;
        existing.IsSuspect = point.IsSuspect;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<long>> RecentCompareCentsAsync(Guid mapId, int take, CancellationToken cancellationToken)
    {
        var rows = await db.PriceCrawlSeriesPoints.AsNoTracking()
            .Where(p => p.MapId == mapId)
            .OrderByDescending(p => p.ObservedAtUtc)
            .Take(Math.Max(take, 1))
            .Select(p => new { p.PriceCents, p.ConvertedCents })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(p => p.ConvertedCents ?? p.PriceCents).ToArray();
    }

    public async Task<IReadOnlyList<PriceCrawlSeries>> QueryByMarketItemAsync(Guid marketItemId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
    {
        var query = db.PriceCrawlSeriesPoints.AsNoTracking().Where(p => p.MarketItemId == marketItemId);
        if (fromUtc is not null)
        {
            query = query.Where(p => p.ObservedAtUtc >= fromUtc);
        }

        if (toUtc is not null)
        {
            query = query.Where(p => p.ObservedAtUtc <= toUtc);
        }

        return await query
            .OrderBy(p => p.ObservedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class PriceCrawlSiteStateStore(DoggoDbContext db) : IPriceCrawlSiteStateStore
{
    public async Task<PriceCrawlSiteState> GetOrAddAsync(string siteKey, CancellationToken cancellationToken)
    {
        var state = await db.PriceCrawlSiteStates
            .FirstOrDefaultAsync(s => s.SiteKey == siteKey, cancellationToken)
            .ConfigureAwait(false);
        if (state is not null)
        {
            return state;
        }

        state = new PriceCrawlSiteState
        {
            Id = Guid.CreateVersion7(),
            SiteKey = siteKey
        };
        db.PriceCrawlSiteStates.Add(state);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return state;
    }

    public async Task SaveAsync(PriceCrawlSiteState state, CancellationToken cancellationToken)
    {
        db.PriceCrawlSiteStates.Update(state);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ExchangeRateReader(DoggoDbContext db) : IExchangeRateReader
{
    public async Task<decimal?> GetRateToCnyAsync(string currencyCode, CancellationToken cancellationToken)
    {
        var code = currencyCode.Trim().ToUpperInvariant();
        if (code == "CNY")
        {
            return 1m;
        }

        return await db.ExchangeRates.AsNoTracking()
            .Where(r => r.CurrencyCode == code)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Spread report across confirmed sites (design §7/§8), computed in one grouped query.</summary>
public sealed class PriceCrawlQueries(DoggoDbContext db) : IPriceCrawlQueries
{
    public async Task<IReadOnlyList<PriceSpreadRow>> SpreadReportAsync(int recentDays, CancellationToken cancellationToken)
    {
        var from = DateTime.UtcNow.AddDays(-Math.Max(recentDays, 1));
        var raw = await db.PriceCrawlSeriesPoints.AsNoTracking()
            .Where(p => !p.IsSuspect && p.ObservedAtUtc >= from && p.ConvertedCents != null)
            .GroupBy(p => p.MarketItemId)
            .Select(g => new
            {
                MarketItemId = g.Key,
                SiteCount = g.Select(p => p.SiteKey).Distinct().Count(),
                Min = g.Min(p => p.ConvertedCents!.Value),
                Max = g.Max(p => p.ConvertedCents!.Value),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return raw
            .Select(r => new PriceSpreadRow(
                r.MarketItemId,
                r.SiteCount,
                r.Min,
                r.Max,
                r.Max - r.Min,
                r.Min == 0 ? 0 : Math.Round((r.Max - r.Min) * 100m / r.Min, 2, MidpointRounding.AwayFromZero)))
            .OrderByDescending(r => r.SpreadPct)
            .ToArray();
    }
}

public static partial class PriceCrawlLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "purged {Count} raw price rows older than {Cutoff:yyyy-MM-dd}")]
    public static partial void RawPurged(ILogger logger, int count, DateTime cutoff);
}
