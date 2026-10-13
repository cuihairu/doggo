using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler.Robots;

public sealed record RobotsRule(string Prefix, bool Allowed);

public sealed record RobotsDecision(bool Allowed, int? CrawlDelaySeconds);

/// <summary>
/// Parsed robots.txt. Evaluation follows the longest-prefix rule: among Allow/Disallow
/// rules that match the path, the longest prefix wins; a tie resolves to Allow.
/// </summary>
public sealed class RobotsRules(IReadOnlyList<RobotsRule> rules, int? crawlDelaySeconds)
{
    private readonly List<RobotsRule> _rules = [.. rules];

    public int? CrawlDelaySeconds { get; } = crawlDelaySeconds;

    public bool HasDisallow => _rules.Any(r => !r.Allowed && r.Prefix.Length > 0);

    public (bool Allowed, int? CrawlDelaySeconds) Decide(string path)
    {
        if (!HasDisallow)
        {
            return (true, CrawlDelaySeconds);
        }

        var pathValue = NormalizePath(path);
        RobotsRule? best = null;
        foreach (var rule in _rules)
        {
            var prefix = NormalizePath(rule.Prefix);
            if (prefix.Length == 0 || !pathValue.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null || prefix.Length > best.Prefix.Length
                || (prefix.Length == best.Prefix.Length && rule.Allowed))
            {
                best = rule;
            }
        }

        return (best?.Allowed ?? true, CrawlDelaySeconds);
    }

    private static string NormalizePath(string path)
    {
        var value = path.Trim();
        return value.Length == 0 ? "/" : value;
    }
}

/// <summary>
/// Fetches, caches and evaluates robots.txt per origin. Fetch failures follow the FailOpen
/// switch (default: treat as unrestricted and log) rather than blocking the whole site.
/// </summary>
public sealed class RobotsChecker(HttpClient http, IOptions<PriceCrawlerOptions> options, ILogger<RobotsChecker> logger) : IDisposable
{
    // Registered as a singleton, so the per-origin robots cache lives with the instance.
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private readonly PriceCrawlerOptions _options = options.Value;

    public async Task<RobotsDecision> IsAllowedAsync(
        string url,
        bool siteRobotsEnabled,
        int? siteIntervalMinutes,
        CancellationToken cancellationToken)
    {
        if (!_options.Robots.Enabled || !siteRobotsEnabled)
        {
            return new RobotsDecision(true, null);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new RobotsDecision(true, null);
        }

        var rules = await GetRulesAsync(uri, cancellationToken).ConfigureAwait(false);
        var (allowed, crawlDelay) = rules.Decide(uri.AbsolutePath);
        return new RobotsDecision(allowed, EffectiveCrawlDelay(crawlDelay, siteIntervalMinutes));
    }

    /// <summary>robots Crawl-delay overrides the site interval only when stricter (§5.3).</summary>
    public static int? EffectiveCrawlDelay(int? crawlDelaySeconds, int? siteIntervalMinutes)
    {
        if (crawlDelaySeconds is not > 0)
        {
            return null;
        }

        var intervalSeconds = siteIntervalMinutes is > 0 ? siteIntervalMinutes.Value * 60 : (int?)null;
        return intervalSeconds is null ? crawlDelaySeconds : Math.Max(crawlDelaySeconds ?? 0, intervalSeconds.Value);
    }

    public void Dispose() => _cache.Dispose();

    private async Task<RobotsRules> GetRulesAsync(Uri url, CancellationToken cancellationToken)
    {
        var robotsUrl = new UriBuilder(url.Scheme, url.Authority, url.Port, "/robots.txt").Uri;
        if (_cache.TryGetValue<RobotsRules>(robotsUrl.AbsoluteUri, out var cached))
        {
            return cached!;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : string.Empty;
            var rules = string.IsNullOrEmpty(text) ? new RobotsRules([], null) : RobotsRulesParser.Parse(text, _options.UserAgent);
            _cache.Set(robotsUrl.AbsoluteUri, rules, TimeSpan.FromHours(Math.Max(_options.Robots.CacheHours, 1)));
            logger.LogDebug("robots.txt loaded for {Origin} with crawl-delay {Delay}s",
                robotsUrl.Authority, rules.CrawlDelaySeconds);
            return rules;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "robots.txt unavailable for {Origin}; fail-open is {FailOpen}",
                robotsUrl.Authority, _options.Robots.FailOpen);
            var rules = _options.Robots.FailOpen
                ? new RobotsRules([], null)
                : new RobotsRules([new RobotsRule("/", false)], null);
            _cache.Set(robotsUrl.AbsoluteUri, rules, TimeSpan.FromHours(Math.Max(_options.Robots.CacheHours, 1)));
            return rules;
        }
    }
}

/// <summary>
/// Minimal robots.txt parser. A group runs from a User-agent line until the directives that
/// follow it; consecutive User-agent lines share one group. Groups whose agent token matches
/// this crawler (or "*") apply; among applicable rules the longest path prefix wins, ties go
/// to Allow. Crawl-delay is the maximum across applicable groups (most conservative).
/// </summary>
public static class RobotsRulesParser
{
    public static RobotsRules Parse(string text, string? userAgent = null)
    {
        var agentName = (userAgent ?? "doggo").ToLowerInvariant();
        var applicable = new List<List<RobotsRule>>();
        var crawlDelay = 0;

        var groupAgents = new List<string>();
        var groupRules = new List<RobotsRule>();
        var groupDelay = 0;

        void Flush()
        {
            if (groupAgents.Count == 0)
            {
                return;
            }

            if (groupAgents.Any(a => a == "*" || agentName.Contains(a, StringComparison.Ordinal)))
            {
                applicable.Add(groupRules);
                if (groupDelay > crawlDelay)
                {
                    crawlDelay = groupDelay;
                }
            }
        }

        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                line = line[..hash].Trim();
            }

            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var field = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();

            switch (field)
            {
                case "user-agent":
                    Flush();
                    groupAgents = [value.ToLowerInvariant()];
                    groupRules = [];
                    groupDelay = 0;
                    break;

                case "allow":
                case "disallow":
                    groupRules.Add(new RobotsRule(value, field == "allow"));
                    break;

                case "crawl-delay":
                    if (int.TryParse(value, out var delay) && delay > 0)
                    {
                        groupDelay = delay;
                    }

                    break;
            }
        }

        Flush();

        var rules = new List<RobotsRule>();
        foreach (var group in applicable)
        {
            rules.AddRange(group);
        }

        return new RobotsRules(rules, crawlDelay > 0 ? crawlDelay : null);
    }
}
