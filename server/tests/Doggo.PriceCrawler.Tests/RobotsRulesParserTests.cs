using Doggo.PriceCrawler.Robots;

namespace Doggo.PriceCrawler.Tests;

public class RobotsRulesParserTests
{
    private const string Text = """
        # robots for example.com
        User-agent: BadBot
        Disallow: /

        User-agent: DoggoPriceBot
        Allow: /market/
        Disallow: /market/private/
        Crawl-delay: 5

        User-agent: *
        Disallow: /admin/
        Crawl-delay: 2
        """;

    [Fact]
    public void MatchesNamedGroupOverWildcard()
    {
        var rules = RobotsRulesParser.Parse(Text, "DoggoPriceBot/1.0 (+price-crawler)");

        var (publicAllowed, delay) = rules.Decide("/market/item/123");
        Assert.True(publicAllowed);
        Assert.Equal(5, delay);

        var (privateAllowed, _) = rules.Decide("/market/private/x");
        // longest prefix wins: /market/private/ (Disallow) beats /market/ (Allow)
        Assert.False(privateAllowed);

        var (adminAllowed, _) = rules.Decide("/admin/settings");
        // named group has no rule for /admin/ but the wildcard group still applies
        Assert.False(adminAllowed);
    }

    [Fact]
    public void CrawlDelayIsMaxOfApplicableGroups()
    {
        var rules = RobotsRulesParser.Parse(Text, "DoggoPriceBot/1.0");
        Assert.Equal(5, rules.CrawlDelaySeconds);
    }

    [Fact]
    public void UnmatchedPathsAreAllowed()
    {
        var rules = RobotsRulesParser.Parse(Text, "DoggoPriceBot/1.0");
        var (allowed, _) = rules.Decide("/open");
        Assert.True(allowed);
    }

    [Fact]
    public void UnknownAgentFallsBackToWildcardGroup()
    {
        var rules = RobotsRulesParser.Parse(Text, "OtherBot/2.0");
        var (allowed, delay) = rules.Decide("/admin/x");
        Assert.False(allowed);
        Assert.Equal(2, delay);
    }

    [Fact]
    public void EmptyFileAllowsEverything()
    {
        var rules = RobotsRulesParser.Parse("", "DoggoPriceBot/1.0");
        Assert.False(rules.HasDisallow);
        var (allowed, delay) = rules.Decide("/anything");
        Assert.True(allowed);
        Assert.Null(delay);
    }

    [Fact]
    public void BareDisallowAllBlocks()
    {
        var rules = RobotsRulesParser.Parse("User-agent: *\nDisallow: /", "doggo");
        Assert.False(rules.Decide("/x").Allowed);
    }

    [Fact]
    public void TiePrefersAllow()
    {
        var rules = RobotsRulesParser.Parse("User-agent: *\nDisallow: /a\nAllow: /a", "doggo");
        Assert.True(rules.Decide("/a/b").Allowed);
    }

    [Fact]
    public void EffectiveCrawlDelayTakesTheStricterWait()
    {
        Assert.Null(RobotsChecker.EffectiveCrawlDelay(null, 10));
        Assert.Null(RobotsChecker.EffectiveCrawlDelay(0, 10));
        // 60s delay vs 10min interval: the interval is stricter.
        Assert.Equal(600, RobotsChecker.EffectiveCrawlDelay(60, 10));
        // 30min delay vs 10min interval: the delay is stricter.
        Assert.Equal(1800, RobotsChecker.EffectiveCrawlDelay(1800, 10));
        // No site interval: delay applies as-is.
        Assert.Equal(30, RobotsChecker.EffectiveCrawlDelay(30, null));
    }
}
