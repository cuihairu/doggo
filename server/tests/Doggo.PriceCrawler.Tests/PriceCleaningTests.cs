using Doggo.PriceCrawler;

namespace Doggo.PriceCrawler.Tests;

public class PriceCleaningTests
{
    [Theory]
    [InlineData(1L, true)]
    [InlineData(100L, true)]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    public void ValidatesRange(long cents, bool expected)
    {
        Assert.Equal(expected, PriceCleaning.IsPriceValid(cents, maxCents: 10_000));
    }

    [Fact]
    public void RejectsAboveMax()
    {
        Assert.False(PriceCleaning.IsPriceValid(10_001, maxCents: 10_000));
    }

    [Theory]
    [InlineData("cny", "CNY")]
    [InlineData("Usd", "USD")]
    [InlineData(" EUR ", "EUR")]
    public void NormalizesCurrency(string code, string expected)
    {
        Assert.True(PriceCleaning.TryNormalizeCurrency(code, out var iso));
        Assert.Equal(expected, iso);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RMB")]
    [InlineData("cny1")]
    [InlineData("XYZ")]
    public void RejectsUnknownCurrency(string? code)
    {
        Assert.False(PriceCleaning.TryNormalizeCurrency(code, out _));
    }

    [Fact]
    public void WindowKeyFloorsToWindow()
    {
        var observed = new DateTime(2026, 10, 13, 7, 47, 31, DateTimeKind.Utc);
        Assert.Equal("202610130700", PriceCleaning.WindowKey(observed, 60));
        Assert.Equal("202610130730", PriceCleaning.WindowKey(observed, 30));
        Assert.Equal("202610130745", PriceCleaning.WindowKey(observed, 15));
    }

    [Fact]
    public void LocalTimeConvertedToUtc()
    {
        var utcNow = DateTime.UtcNow;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.Local);
        Assert.Equal(PriceCleaning.WindowKey(utcNow, 60), PriceCleaning.WindowKey(local, 60));
    }

    [Fact]
    public void SuspectWhenFarFromMedian()
    {
        var recent = new long[] { 100, 101, 99, 100, 102, 100, 101, 99, 100, 100 };
        Assert.True(PriceCleaning.IsSuspect(200, recent, thresholdPct: 50));
        Assert.False(PriceCleaning.IsSuspect(110, recent, thresholdPct: 50));
    }

    [Fact]
    public void EmptyHistoryIsNeverSuspect()
    {
        Assert.False(PriceCleaning.IsSuspect(500, [], thresholdPct: 50));
    }

    [Fact]
    public void ConvertPropagatesNullRate()
    {
        Assert.Null(PriceCleaning.ConvertCents(100, null));
        Assert.Equal(725, PriceCleaning.ConvertCents(100, 7.25m));
    }

    [Fact]
    public void MedianIgnoresOrder()
    {
        Assert.Equal(100, PriceCleaning.Median([99, 100, 101, 50, 150]));
    }
}
