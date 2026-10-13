namespace Doggo.PriceCrawler.Html;

/// <summary>
/// Selector-driven site configuration (bound from the per-site "Html" block). Templates use
/// {query} for discovery and {id} for detail URLs.
/// </summary>
public sealed class HtmlSiteAdapterOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string SearchPathTemplate { get; set; } = "/search?q={query}";

    public string DetailPathTemplate { get; set; } = "/item/{id}";

    public string ItemSelector { get; set; } = ".market-item";

    /// <summary>Attribute of the item element holding the external id.</summary>
    public string ItemIdAttribute { get; set; } = "data-id";

    public string TitleSelector { get; set; } = ".name";

    public string PriceSelector { get; set; } = ".price";

    public string CurrencyCode { get; set; } = "CNY";
}
