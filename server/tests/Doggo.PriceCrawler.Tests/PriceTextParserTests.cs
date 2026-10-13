using Doggo.PriceCrawler;

namespace Doggo.PriceCrawler.Tests;

public class PriceTextParserTests
{
    [Theory]
    [InlineData("1284", 128400L)]
    [InlineData("¥1,284.50", 128450L)]
    [InlineData("$0.99", 99L)]
    [InlineData("12.8 元", 1280L)]
    [InlineData(" 1 284 ", 128400L)]
    [InlineData("￥99.00", 9900L)]
    public void ParsesCommonPriceText(string text, long expected)
    {
        Assert.True(PriceTextParser.TryParse(text, out var cents));
        Assert.Equal(expected, cents);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("面议")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("free")]
    public void RejectsNonPriceText(string? text)
    {
        Assert.False(PriceTextParser.TryParse(text, out var cents));
        Assert.Equal(0, cents);
    }

    [Fact]
    public void RoundsHalfUp()
    {
        Assert.True(PriceTextParser.TryParse("0.125", out var cents));
        Assert.Equal(13, cents);
    }
}
