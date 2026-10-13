using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Doggo.PriceCrawler.Html;

/// <summary>
/// Config-driven HTML adapter (see Doggo.Scraper's HtmlSourceAdapter for the pattern):
/// CSS selectors come from the site's Html block, so adding a site is configuration, not code.
/// Currency is never converted here — conversion happens in the ingestor.
/// </summary>
public sealed class ConfiguredHtmlSiteAdapter(
    HttpClient http,
    string siteKey,
    HtmlSiteAdapterOptions options,
    ILogger<ConfiguredHtmlSiteAdapter> logger) : ISiteAdapter
{
    public string SiteKey => siteKey;
    public string AdapterVersion => "1.0";
    public IReadOnlyList<string> SupportedCurrencyCodes => [options.CurrencyCode.Trim().ToUpperInvariant()];
    public string RobotsUrl => options.BaseUrl.TrimEnd('/') + "/";
    public SiteRateLimit SuggestedRateLimit => new(1, 1, null);

    private readonly HtmlParser _parser = new();

    public async Task<IReadOnlyList<ExternalItemRef>> DiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken)
    {
        var keyword = query.Keyword ?? query.MarketHashName ?? string.Empty;
        if (keyword.Length == 0 || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return [];
        }

        var searchPath = options.SearchPathTemplate.Contains("{query}")
            ? options.SearchPathTemplate.Replace("{query}", Uri.EscapeDataString(keyword))
            : options.SearchPathTemplate;
        var searchUrl = new Uri(baseUri, searchPath);
        var html = await FetchAsync(searchUrl.ToString(), cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return [];
        }

        var refs = new List<ExternalItemRef>();
        foreach (var item in Parse(html).QuerySelectorAll(options.ItemSelector))
        {
            var externalId = (item.GetAttribute(options.ItemIdAttribute) ?? string.Empty).Trim();
            if (externalId.Length == 0)
            {
                continue;
            }

            var title = options.TitleSelector.Length > 0
                ? (item.QuerySelector(options.TitleSelector)?.TextContent ?? string.Empty).Trim()
                : string.Empty;
            refs.Add(new ExternalItemRef(externalId, title, ResolveDetailUrl(baseUri, externalId)));
        }

        return refs;
    }

    public async Task<IReadOnlyList<ExternalPriceFetch>> FetchPricesAsync(
        IReadOnlyList<string> externalIds, CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0 || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return [];
        }

        var currency = options.CurrencyCode.Trim().ToUpperInvariant();
        var results = new List<ExternalPriceFetch>();

        for (var i = 0; i < externalIds.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(SuggestedRateLimit.MinIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }

            var externalId = externalIds[i];
            var html = await FetchAsync(ResolveDetailUrl(baseUri, externalId), cancellationToken).ConfigureAwait(false);
            if (html is null)
            {
                continue;
            }

            var element = Parse(html).QuerySelector(options.PriceSelector);
            var text = (element?.TextContent ?? string.Empty).Trim();
            if (!PriceTextParser.TryParse(text, out var cents))
            {
                logger.LogWarning("No parsable price on {Site} item {ExternalId} (text: {Text})", siteKey, externalId, text);
                continue;
            }

            results.Add(new ExternalPriceFetch(
                externalId,
                cents,
                currency,
                DateTimeOffset.UtcNow,
                text,
                PriceFingerprint.Compute(html)));
        }

        return results;
    }

    private IDocument Parse(string html) => _parser.ParseDocument(html);

    private async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Price fetch {Url} returned {Status}", url, (int)response.StatusCode);
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveDetailUrl(Uri baseUri, string externalId)
    {
        var path = options.DetailPathTemplate.Replace("{id}", Uri.EscapeDataString(externalId));
        return new Uri(baseUri, path).ToString();
    }
}
