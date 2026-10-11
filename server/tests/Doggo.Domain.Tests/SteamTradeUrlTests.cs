using Doggo.Domain.Users;
using Xunit;

namespace Doggo.Domain.Tests;

public class SteamTradeUrlTests
{
    [Fact]
    public void TryParse_reads_partner_token_and_derives_steamid64()
    {
        var url = "https://steamcommunity.com/tradeoffer/new/?partner=816475594&token=abc1234567";
        Assert.True(SteamTradeUrl.TryParse(url, out var parts));

        Assert.Equal("816475594", parts.Partner);
        Assert.Equal("abc1234567", parts.Token);
        Assert.Equal((76561197960265728UL + 816475594UL).ToString(), parts.SteamId);
    }

    [Fact]
    public void TryParse_accepts_token_before_partner()
    {
        var url = "https://steamcommunity.com/tradeoffer/new/?token=zZxX998877&partner=100200";
        Assert.True(SteamTradeUrl.TryParse(url, out var parts));

        Assert.Equal("100200", parts.Partner);
        Assert.Equal("zZxX998877", parts.Token);
    }

    [Theory]
    [InlineData("http://steamcommunity.com/tradeoffer/new/?partner=1&token=abcdef")]
    [InlineData("https://STEAMCOMMUNITY.com/tradeoffer/new/?partner=1&token=abcdef")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=42&token=abcdef#offer2")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=42&token=abcdef&snr=2.2.1")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?token=ab12%2D34567&partner=42")]
    public void TryParse_accepts_scheme_fragment_and_encoding_variants(string url) =>
        Assert.True(SteamTradeUrl.TryParse(url, out _));

    [Theory]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=123")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?token=abcdef")]
    [InlineData("https://steamcommunity.com/profiles/76561198000000000")]
    [InlineData("https://evil.example.com/tradeoffer/new/?partner=123&token=abcdef")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=abc&token=abcdef")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=123&token=short")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/?partner=0&token=abcdef")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_rejects_malformed_inputs(string url) =>
        Assert.False(SteamTradeUrl.TryParse(url, out _));

    [Fact]
    public void TryParse_rejects_null_and_fills_empty_parts()
    {
        Assert.False(SteamTradeUrl.TryParse(null, out var parts));
        Assert.Equal(new TradeUrlParts(string.Empty, string.Empty, string.Empty), parts);
    }
}
