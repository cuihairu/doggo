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

public sealed record ExchangeRateDto(string CurrencyCode, decimal Rate, string Source);

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/market/items", ListItems);
        app.MapGet("/api/market/items/{id:guid}/prices", GetPrices);
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
