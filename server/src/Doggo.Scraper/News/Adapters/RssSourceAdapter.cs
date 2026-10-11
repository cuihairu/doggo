using System.Globalization;
using System.Xml.Linq;
using Doggo.Domain.News;

namespace Doggo.Scraper.News.Adapters;

/// <summary>Adapter for RSS 2.0 and Atom feeds.</summary>
public sealed class RssSourceAdapter(HttpClient http) : INewsSourceAdapter
{
    public NewsSourceType SourceType => NewsSourceType.Rss;

    public async Task<IReadOnlyList<NewsCandidate>> FetchAsync(NewsSource source, CancellationToken cancellationToken)
    {
        var xml = await http.GetStringAsync(source.Url, cancellationToken).ConfigureAwait(false);
        return Parse(xml);
    }

    internal static IReadOnlyList<NewsCandidate> Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (Exception)
        {
            return [];
        }

        var root = document.Root;
        if (root is null)
        {
            return [];
        }

        return root.Name.LocalName.Equals("rss", StringComparison.OrdinalIgnoreCase)
            ? ParseRss(document)
            : ParseAtom(document);
    }

    private static List<NewsCandidate> ParseRss(XDocument document)
    {
        var items = new List<NewsCandidate>();
        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var title = Text(item, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var link = Text(item, "link");
            if (string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            var published = Date(item, "pubDate", "date", "published", "updated", "created", "modified");
            var categories = item.Descendants()
                .Where(e => e.Name.LocalName == "category" && e.Value.Trim().Length > 0)
                .Select(e => e.Value.Trim())
                .ToList();
            var enclosureUrl = MediaUrl(item);

            items.Add(new NewsCandidate(
                Title: title.Trim(),
                Url: link.Trim(),
                PublishedAtUtc: published,
                Category: categories.Count > 0 ? categories[0] : "",
                Tags: string.Join(',', categories),
                Summary: Summary(Text(item, "description", "summary"), Text(item, "encoded")),
                ContentHtml: Html(Text(item, "encoded"), Text(item, "content"), Text(item, "description")),
                Author: Author(Text(item, "creator"), Text(item, "author")),
                CoverUrl: enclosureUrl));
        }

        return items;
    }

    private static List<NewsCandidate> ParseAtom(XDocument document)
    {
        var items = new List<NewsCandidate>();
        foreach (var entry in document.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            var title = Text(entry, "title");
            var link = AtomLink(entry);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            items.Add(new NewsCandidate(
                Title: title.Trim(),
                Url: link.Trim(),
                PublishedAtUtc: Date(entry, "updated", "published", "issued", "created", "modified"),
                Category: entry.Descendants()
                    .Where(e => e.Name.LocalName == "category" && e.Attribute("term") is not null)
                    .Select(e => e.Attribute("term")!.Value.Trim())
                    .FirstOrDefault() ?? "",
                Tags: string.Join(',', entry.Descendants()
                    .Where(e => e.Name.LocalName == "category" && e.Attribute("term") is not null)
                    .Select(e => e.Attribute("term")!.Value.Trim())),
                Summary: Text(entry, "summary", "description"),
                ContentHtml: Text(entry, "content"),
                Author: Author(
                    entry.Descendants().FirstOrDefault(e => e.Name.LocalName == "name")?.Value,
                    null),
                CoverUrl: MediaUrl(entry)));
        }

        return items;
    }

    private static string? Text(XElement container, params string[] localNames)
    {
        foreach (var name in localNames)
        {
            var direct = container.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (direct is not null && direct.Value.Trim().Length > 0)
            {
                return direct.Value.Trim();
            }

            var nested = container.Descendants().FirstOrDefault(e => e.Name.LocalName == name && e.Value.Trim().Length > 0);
            if (nested is not null)
            {
                return nested.Value.Trim();
            }
        }

        return null;
    }

    private static string? AtomLink(XElement container)
    {
        var links = container.Elements().Where(e => e.Name.LocalName == "link").ToList();
        var alternate = links.FirstOrDefault(e =>
            e.Attribute("rel") is null || e.Attribute("rel")!.Value.Equals("alternate", StringComparison.OrdinalIgnoreCase));
        return (alternate ?? links.FirstOrDefault())?.Attribute("href")?.Value.Trim();
    }

    private static DateTime Date(XElement container, params string[] localNames)
    {
        foreach (var name in localNames)
        {
            var value = Text(container, name);
            if (value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return DateTime.UtcNow;
    }

    private static string? Author(string? creator, string? author)
    {
        if (!string.IsNullOrWhiteSpace(creator))
        {
            return creator.Trim();
        }

        return string.IsNullOrWhiteSpace(author) ? null : author!.Trim();
    }

    private static string? MediaUrl(XElement container)
    {
        var enclosure = container.Elements().FirstOrDefault(e => e.Name.LocalName == "enclosure");
        if (enclosure?.Attribute("url") is { } urlAttribute && urlAttribute.Value.Trim().Length > 0)
        {
            return urlAttribute.Value.Trim();
        }

        var thumbnail = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "thumbnail");
        return thumbnail?.Attribute("url")?.Value.Trim();
    }

    private static string? Summary(string? description, string? content) =>
        description is not null ? description : content;

    private static string? Html(string? encoded, string? content, string? description) =>
        encoded ?? (content ?? (description is not null && description.Contains('<') ? description : null));
}
