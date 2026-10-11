using System.Globalization;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Doggo.Domain.News;
using Microsoft.Extensions.Logging;

namespace Doggo.Scraper.News.Adapters;

public sealed record HtmlAdapterOptions
{
    public string ItemSelector { get; set; } = "";
    public string TitleSelector { get; set; } = "";
    public string LinkSelector { get; set; } = "";
    public string DateSelector { get; set; } = "";
    public string ContentSelector { get; set; } = "";
    public string SummarySelector { get; set; } = "";
    public string AuthorSelector { get; set; } = "";
    public string? CoverSelector { get; set; }
    public string? Category { get; set; }
}

/// <summary>
/// Generic adapter for HTML pages, driven by CSS selectors configured in the source's
/// <c>ConfigJson</c>. Works on static pages through <see cref="IPageFetcher"/>; sources that
/// render content with JavaScript should enable Scraper:EnablePlaywright.
/// </summary>
public sealed class HtmlSourceAdapter(IPageFetcher fetcher) : INewsSourceAdapter
{
    private static readonly HtmlParser Parser = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public NewsSourceType SourceType => NewsSourceType.Html;

    public async Task<IReadOnlyList<NewsCandidate>> FetchAsync(NewsSource source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ConfigJson))
        {
            throw new InvalidOperationException($"Source '{source.Name}' has no selector configuration.");
        }

        HtmlAdapterOptions options;
        try
        {
            options = JsonSerializer.Deserialize<HtmlAdapterOptions>(source.ConfigJson, JsonOptions)
                ?? throw new InvalidOperationException($"Source '{source.Name}' has invalid selector configuration.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Source '{source.Name}' has invalid selector configuration.", ex);
        }

        if (string.IsNullOrWhiteSpace(options.ItemSelector))
        {
            throw new InvalidOperationException($"Source '{source.Name}' is missing itemSelector.");
        }

        var baseUrl = new Uri(source.Url);
        var html = await fetcher.FetchAsync(source.Url, cancellationToken).ConfigureAwait(false);
        return Parse(html, baseUrl, options);
    }

    internal static IReadOnlyList<NewsCandidate> Parse(string html, Uri baseUrl, HtmlAdapterOptions options)
    {
        var document = Parser.ParseDocument(html);
        var candidates = new List<NewsCandidate>();

        foreach (var element in document.QuerySelectorAll(options.ItemSelector))
        {
            var link = ResolveUrl(baseUrl, QueryText(element, options.LinkSelector, attribute: "href"));
            if (link is null)
            {
                continue;
            }

            var title = QueryText(element, options.TitleSelector)
                ?? QueryText(element, "h1,h2,h3,h4,h5,h6")
                ?? QueryText(element, "a");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var content = QueryHtml(element, options.ContentSelector);
            var summary = QueryText(element, options.SummarySelector);
            candidates.Add(new NewsCandidate(
                Title: title.Trim(),
                Url: link,
                PublishedAtUtc: Date(element, options.DateSelector),
                Category: options.Category ?? "",
                Tags: "",
                Summary: summary,
                ContentHtml: content,
                Author: QueryText(element, options.AuthorSelector),
                CoverUrl: ResolveUrl(baseUrl, QueryText(element, options.CoverSelector ?? "img", attribute: "src"))));
        }

        return candidates;
    }

    private static string? QueryText(IElement container, string selector, string? attribute = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var element = container.QuerySelector(selector) ?? container;
        var value = attribute is null ? element.TextContent : element.GetAttribute(attribute);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? QueryHtml(IElement container, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        return container.QuerySelector(selector)?.InnerHtml;
    }

    private static DateTime Date(IElement container, string selector)
    {
        var value = QueryText(container, selector, attribute: "datetime") ?? QueryText(container, selector);
        if (value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }

    private static string? ResolveUrl(Uri baseUrl, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        return Uri.TryCreate(baseUrl, relative, out var absolute) ? absolute.AbsoluteUri : null;
    }
}
