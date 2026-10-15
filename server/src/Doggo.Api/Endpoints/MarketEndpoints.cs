using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public sealed record MarketItemQuery(int Page = 1, int PageSize = 20, string? Q = null, GameKind? Game = null, MarketItemType? Type = null);

public sealed record MarketItemDto(
    Guid Id,
    string ClassId,
    string MarketHashName,
    MarketItemType ItemType,
    GameKind Game,
    string IconUrl,
    long? LatestPriceCents,
    string? Currency,
    DateTime? PriceUpdatedAtUtc,
    long? ChangeCents,
    int? ChangeBps);

public sealed record MarketPricePoint(DateOnly Date, long OpenCents, long HighCents, long LowCents, long CloseCents);

/// <summary>
/// External reference view (price crawler design §7): median of each confirmed site's newest
/// usable crawled price vs the in-site daily close. Aggregated on purpose — per-site rows stay
/// admin-only; suspect or unconverted observations never feed the number.
/// </summary>
public sealed record MarketReferenceDto(
    Guid MarketItemId,
    int SiteCount,
    long? ExternalMedianCents,
    DateTime? ExternalObservedAtUtc,
    long? InSiteCloseCents,
    decimal? SpreadPct);

public sealed record ExchangeRateDto(string CurrencyCode, decimal Rate, string Source);

public static class MarketEndpoints
{
    /// <summary>How far back the external reference looks for a per-site price.</summary>
    private const int ReferenceWindowDays = 7;

    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/market/items", ListItems);
        app.MapGet("/api/market/items/{id:guid}/prices", GetPrices);
        app.MapGet("/api/market/items/{id:guid}/reference", GetReference);
        app.MapGet("/api/exchange-rates", GetExchangeRates);
        return app;
    }

    /// <summary>
    /// Catalog listing with the latest stored price. Prices come from the local snapshot
    /// table, so the API keeps working while vapor is unreachable.
    /// </summary>
    private static async Task<IResult> ListItems(DoggoDbContext db, [AsParameters] MarketItemQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(query.Page, 1, 100_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = db.MarketItems.AsNoTracking().AsQueryable();
        if (query.Game.HasValue)
        {
            items = items.Where(i => i.Game == query.Game.Value);
        }

        if (query.Type.HasValue)
        {
            items = items.Where(i => i.ItemType == query.Type.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            // ToLower on both sides keeps behavior identical across MySQL (CI-insensitive
            // collation) and the InMemory provider used in tests (ordinal).
            items = items.Where(i => i.MarketHashName.ToLower().Contains(q) || i.ClassId.ToLower().Contains(q));
        }

        var total = await items.CountAsync(cancellationToken);
        var pageItems = await items
            .OrderBy(i => i.MarketHashName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = new List<MarketItemDto>(pageItems.Count);
        foreach (var item in pageItems)
        {
            var latest = await db.MarketPriceSnapshots
                .AsNoTracking()
                .Where(s => s.MarketItemId == item.Id)
                .OrderByDescending(s => s.CapturedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            // Day change for the list badge: the two newest daily closes. With a
            // single close there is nothing to compare, so it stays null rather
            // than reporting a change against zero.
            var closes = await db.MarketPriceDailies
                .AsNoTracking()
                .Where(d => d.MarketItemId == item.Id)
                .OrderByDescending(d => d.Date)
                .Select(d => d.CloseCents)
                .Take(2)
                .ToListAsync(cancellationToken);

            long? changeCents = null;
            int? changeBps = null;
            if (closes.Count == 2)
            {
                changeCents = closes[0] - closes[1];
                changeBps = closes[1] == 0
                    ? null
                    : (int)Math.Round(changeCents.Value * 10000m / closes[1]);
            }

            dtos.Add(new MarketItemDto(
                item.Id,
                item.ClassId,
                item.MarketHashName,
                item.ItemType,
                item.Game,
                item.IconUrl,
                latest?.PriceCents,
                latest?.Currency,
                latest?.CapturedAtUtc,
                changeCents,
                changeBps));
        }

        return Results.Ok(new { total, page, pageSize, items = dtos });
    }

    /// <summary>Daily OHLC series for trend charts, newest first.</summary>
    private static async Task<IResult> GetPrices(DoggoDbContext db, Guid id, [FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        var item = await db.MarketItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { message = "Market item not found." });
        }

        var limit = Math.Clamp(days, 1, 365);
        var points = await db.MarketPriceDailies
            .AsNoTracking()
            .Where(d => d.MarketItemId == id)
            .OrderByDescending(d => d.Date)
            .Take(limit)
            .Select(d => new MarketPricePoint(d.Date, d.OpenCents, d.HighCents, d.LowCents, d.CloseCents))
            .ToListAsync(cancellationToken);

        return Results.Ok(new { item = id, points });
    }

    /// <summary>
    /// External reference price for the front-end: each confirmed site's newest usable crawled
    /// price, then the median across sites, compared against the in-site daily close. No series
    /// rows yields nulls rather than an error — the crawler may simply not cover the item.
    /// </summary>
    private static async Task<IResult> GetReference(DoggoDbContext db, Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.MarketItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { message = "Market item not found." });
        }

        // Only Confirmed maps reach the series table; converted + non-suspect points are the ones
        // a reader can rely on. The newest such point per site is that site's reference value.
        var from = DateTime.UtcNow.AddDays(-ReferenceWindowDays);
        var candidates = await db.PriceCrawlSeriesPoints.AsNoTracking()
            .Where(p => p.MarketItemId == id && !p.IsSuspect && p.ConvertedCents != null && p.ObservedAtUtc >= from)
            .Select(p => new { p.SiteKey, p.ObservedAtUtc, p.ConvertedCents })
            .ToListAsync(cancellationToken);

        var perSite = candidates
            .GroupBy(p => p.SiteKey)
            .Select(g => g.OrderByDescending(p => p.ObservedAtUtc).First())
            .ToList();

        long? medianCents = null;
        DateTime? observedAtUtc = null;
        var siteCount = 0;
        if (perSite.Count > 0)
        {
            var values = perSite.Select(p => p.ConvertedCents!.Value).OrderBy(v => v).ToList();
            siteCount = values.Count;
            observedAtUtc = perSite.Max(p => p.ObservedAtUtc);
            medianCents = values.Count % 2 == 1
                ? values[(values.Count - 1) / 2]
                : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2;
        }

        // In-site baseline is the latest daily close (price crawler design §7.1).
        long? inSiteCloseCents = await db.MarketPriceDailies
            .AsNoTracking()
            .Where(d => d.MarketItemId == id)
            .OrderByDescending(d => d.Date)
            .Select(d => (long?)d.CloseCents)
            .FirstOrDefaultAsync(cancellationToken);

        decimal? spreadPct = medianCents.HasValue && inSiteCloseCents is > 0
            ? Math.Round((medianCents.Value - inSiteCloseCents.Value) * 100m / inSiteCloseCents.Value, 2, MidpointRounding.AwayFromZero)
            : null;

        return Results.Ok(new MarketReferenceDto(id, siteCount, medianCents, observedAtUtc, inSiteCloseCents, spreadPct));
    }

    private static async Task<IResult> GetExchangeRates(DoggoDbContext db, CancellationToken cancellationToken)
    {
        var rates = await db.ExchangeRates
            .AsNoTracking()
            .OrderBy(r => r.CurrencyCode)
            .Select(r => new ExchangeRateDto(r.CurrencyCode, r.Rate, r.Source))
            .ToListAsync(cancellationToken);

        return Results.Ok(new { items = rates });
    }
}
