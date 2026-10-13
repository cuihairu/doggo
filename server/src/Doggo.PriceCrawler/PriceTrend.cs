using Doggo.Domain.Price;

namespace Doggo.PriceCrawler;

public sealed record PriceDailyClose(DateOnly Date, long CloseCents);

public sealed record PriceTrendPoint(DateOnly Date, long CloseCents, decimal? DayOverDayPct, decimal? WeekOverWeekPct);

/// <summary>
/// Query-time trend metrics from the series (design §7: no trend table).
/// Daily close = the last non-suspect point per UTC day, unified in CNY
/// (ConvertedCents, falling back to PriceCents which is CNY after normalization).
/// </summary>
public static class PriceTrend
{
    public static IReadOnlyList<PriceDailyClose> DailyCloses(IEnumerable<PriceCrawlSeries> points)
    {
        return points
            .Where(p => !p.IsSuspect)
            .GroupBy(p => DateOnly.FromDateTime(p.ObservedAtUtc))
            .Select(g => g.OrderBy(p => p.ObservedAtUtc).Last())
            .OrderBy(p => p.ObservedAtUtc)
            .Select(p => new PriceDailyClose(DateOnly.FromDateTime(p.ObservedAtUtc), CompareCents(p)))
            .ToArray();
    }

    public static IReadOnlyList<PriceTrendPoint> Build(IReadOnlyList<PriceDailyClose> closes)
    {
        var result = new List<PriceTrendPoint>(closes.Count);
        for (var i = 0; i < closes.Count; i++)
        {
            var current = closes[i];
            var previous = i > 0 ? closes[i - 1].CloseCents : (long?)null;
            var weekAgo = closes.FirstOrDefault(c => c.Date == current.Date.AddDays(-7))?.CloseCents;
            result.Add(new PriceTrendPoint(current.Date, current.CloseCents, PctChange(previous, current.CloseCents), PctChange(weekAgo, current.CloseCents)));
        }

        return result;
    }

    /// <summary>区间涨跌: first vs last close inside the trailing <paramref name="days"/> window.</summary>
    public static decimal? PeriodChangePct(IReadOnlyList<PriceDailyClose> closes, int days)
    {
        if (closes.Count == 0 || days <= 0)
        {
            return null;
        }

        var last = closes[^1];
        var windowStart = last.Date.AddDays(-(days - 1));
        var first = closes.FirstOrDefault(c => c.Date >= windowStart);
        // A window holding a single day has no "change" to report.
        if (first is null || first.Date == last.Date)
        {
            return null;
        }

        return PctChange(first.CloseCents, last.CloseCents);
    }

    public static decimal? PctChange(long? from, long? to)
    {
        if (from is not > 0 || to is null)
        {
            return null;
        }

        return Math.Round((to.Value - from.Value) * 100m / from.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static long CompareCents(PriceCrawlSeries point) => point.ConvertedCents ?? point.PriceCents;
}
