using Doggo.Domain.Cases;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Cases;

public sealed class PrizeNotFoundException(string message) : Exception(message)
{
}

public sealed record PrizeSummary(
    Guid Id,
    Guid OrderId,
    PrizeSource Source,
    string AssetId,
    string ClassId,
    string InstanceId,
    string MarketHashName,
    CaseQuality Quality,
    string Exterior,
    double? FloatValue,
    string Rarity,
    string IconUrl,
    string? InspectLink,
    long PriceCents,
    DateTime? TradableAtUtc,
    long? MarketPriceCents,
    DateTime? MarketPriceAtUtc,
    DateTime CreatedAtUtc);

public sealed record PrizePage(IReadOnlyList<PrizeSummary> Items, int Page, int PageSize, int Total);

public sealed record PrizeLogView(Guid PrizeId, PrizeSource Source, Guid? OrderId, DateTime CreatedAtUtc);

/// <summary>
/// Read side of prize records: a user's prize list and detail, the opening logs behind a
/// prize, and the market valuation attached from the latest price snapshot of the matching
/// market item (by CS2 market hash name). No snapshot or no matching item means no
/// valuation — the record-time price stays authoritative.
/// </summary>
public sealed partial class PrizeService(DoggoDbContext db)
{
    public async Task<PrizePage> ListPrizesAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await db.UserPrizes.AsNoTracking()
            .CountAsync(p => p.UserId == userId, cancellationToken);
        var prizes = await db.UserPrizes.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var valuations = await LoadValuationsAsync(prizes, cancellationToken);
        return new PrizePage(
            prizes.Select(p => ToSummary(p, valuations.GetValueOrDefault(p.MarketHashName))).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<PrizeSummary> GetPrizeAsync(Guid userId, Guid prizeId, CancellationToken cancellationToken = default)
    {
        var prize = await db.UserPrizes.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == prizeId && p.UserId == userId, cancellationToken)
            ?? throw new PrizeNotFoundException($"Prize {prizeId} was not found.");

        var valuations = await LoadValuationsAsync([prize], cancellationToken);
        return ToSummary(prize, valuations.GetValueOrDefault(prize.MarketHashName));
    }

    public async Task<IReadOnlyList<PrizeLogView>> ListLogsAsync(
        Guid userId, Guid prizeId, CancellationToken cancellationToken = default)
    {
        // Ownership check first: logs of somebody else's prize are not visible.
        var owns = await db.UserPrizes.AsNoTracking()
            .AnyAsync(p => p.Id == prizeId && p.UserId == userId, cancellationToken);
        if (!owns)
        {
            throw new PrizeNotFoundException($"Prize {prizeId} was not found.");
        }

        var logs = await db.UserPrizeLogs.AsNoTracking()
            .Where(l => l.PrizeId == prizeId)
            .OrderBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return logs.Select(l => new PrizeLogView(l.PrizeId, l.Source, l.OrderId, l.CreatedAtUtc)).ToList();
    }

    private async Task<Dictionary<string, (long PriceCents, DateTime AtUtc)>> LoadValuationsAsync(
        IReadOnlyList<UserPrize> prizes, CancellationToken cancellationToken)
    {
        var valuations = new Dictionary<string, (long, DateTime)>();
        var names = prizes
            .Select(p => p.MarketHashName)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (names.Count == 0)
        {
            return valuations;
        }

        var items = await db.MarketItems.AsNoTracking()
            .Where(m => m.Game == GameKind.Cs2 && names.Contains(m.MarketHashName))
            .Select(m => new { m.Id, m.MarketHashName })
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var latest = await db.MarketPriceSnapshots.AsNoTracking()
                .Where(s => s.MarketItemId == item.Id)
                .OrderByDescending(s => s.CapturedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                valuations[item.MarketHashName] = (latest.PriceCents, latest.CapturedAtUtc);
            }
        }

        return valuations;
    }

    private static PrizeSummary ToSummary(UserPrize prize, (long PriceCents, DateTime AtUtc) valuation) => new(
        prize.Id,
        prize.OrderId,
        prize.Source,
        prize.AssetId,
        prize.ClassId,
        prize.InstanceId,
        prize.MarketHashName,
        prize.Quality,
        prize.Exterior,
        prize.FloatValue,
        prize.Rarity,
        prize.IconUrl,
        prize.InspectLink,
        prize.PriceCents,
        prize.TradableAtUtc,
        valuation.PriceCents == 0 ? null : valuation.PriceCents,
        valuation.AtUtc == default ? null : valuation.AtUtc,
        prize.CreatedAtUtc);
}
