using Doggo.Domain.Price;
using Doggo.PriceCrawler;

namespace Doggo.PriceCrawler.Tests;

public class PriceTrendTests
{
    private static PriceCrawlSeries Point(DateOnly date, int hour, long cents, bool suspect = false) =>
        new()
        {
            ObservedAtUtc = date.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc),
            PriceCents = cents,
            ConvertedCents = cents,
            IsSuspect = suspect
        };

    [Fact]
    public void DailyCloseIsLastNonSuspectPointOfTheDay()
    {
        var day = new DateOnly(2026, 10, 12);
        var points = new[]
        {
            Point(day, 1, 100),
            Point(day, 5, 110, suspect: true),
            Point(day, 9, 120),
        };
        var closes = PriceTrend.DailyCloses(points);
        var close = Assert.Single(closes);
        Assert.Equal(day, close.Date);
        Assert.Equal(120, close.CloseCents);
    }

    [Fact]
    public void DaysWithOnlySuspectPointsAreDropped()
    {
        var day = new DateOnly(2026, 10, 12);
        var closes = PriceTrend.DailyCloses([Point(day, 3, 100, suspect: true)]);
        Assert.Empty(closes);
    }

    [Fact]
    public void BuildComputesDayOverDay()
    {
        var closes = new[]
        {
            new PriceDailyClose(new DateOnly(2026, 10, 11), 100),
            new PriceDailyClose(new DateOnly(2026, 10, 12), 110),
            new PriceDailyClose(new DateOnly(2026, 10, 13), 99),
        };
        var trend = PriceTrend.Build(closes);

        Assert.Null(trend[0].DayOverDayPct);
        Assert.Equal(10, trend[1].DayOverDayPct);
        Assert.Equal(-10, trend[2].DayOverDayPct);
    }

    [Fact]
    public void WeekOverWeekUsesCloseSevenDaysEarlier()
    {
        var closes = new[]
        {
            new PriceDailyClose(new DateOnly(2026, 10, 6), 100),
            new PriceDailyClose(new DateOnly(2026, 10, 13), 150),
        };
        var trend = PriceTrend.Build(closes);
        Assert.Equal(50, trend[1].WeekOverWeekPct);
        Assert.Null(trend[0].WeekOverWeekPct);
    }

    [Fact]
    public void MissingWeekIsNull()
    {
        var closes = new[] { new PriceDailyClose(new DateOnly(2026, 10, 13), 150) };
        Assert.Null(PriceTrend.Build(closes)[0].WeekOverWeekPct);
    }

    [Fact]
    public void PeriodChangeUsesWindowBounds()
    {
        var closes = new[]
        {
            new PriceDailyClose(new DateOnly(2026, 10, 7), 100),
            new PriceDailyClose(new DateOnly(2026, 10, 10), 80),
            new PriceDailyClose(new DateOnly(2026, 10, 13), 120),
        };
        // 7-day window starts at 10-07: 100 → 120.
        Assert.Equal(20, PriceTrend.PeriodChangePct(closes, 7));
        // 2-day window only contains 10-13.
        Assert.Null(PriceTrend.PeriodChangePct(closes, 2));
    }

    [Fact]
    public void PctChangeGuardsNonPositiveBase()
    {
        Assert.Null(PriceTrend.PctChange(0, 100));
        Assert.Null(PriceTrend.PctChange(null, 100));
        Assert.Null(PriceTrend.PctChange(100, null));
        Assert.Equal(50, PriceTrend.PctChange(200, 300));
    }
}
