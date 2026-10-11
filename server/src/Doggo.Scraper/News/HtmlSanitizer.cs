using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Doggo.Scraper.News;

public sealed class HtmlSanitizerOptions
{
    /// <summary>
    /// When set, remote image sources are rewritten to `<proxy>?url=<original>` so that
    /// third-party hosts do not see the platform domain (referrer leaks, hotlink abuse).
    /// When empty, original image URLs are kept.
    /// </summary>
    public string? ImageProxyBaseUrl { get; set; }
}

/// <summary>
/// Whitelist-based HTML cleaning for scraped article bodies: unsafe elements are dropped,
/// unknown wrappers are unwrapped, and every attribute is re-checked against a small allowlist.
/// </summary>
public sealed class HtmlSanitizer(HtmlSanitizerOptions? options = null)
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "pre", "code", "blockquote", "cite", "q", "ul", "ol", "li",
        "dl", "dt", "dd", "h1", "h2", "h3", "h4", "h5", "h6", "a", "img", "figure",
        "figcaption", "strong", "b", "em", "i", "u", "s", "del", "ins", "sub", "sup",
        "span", "small", "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td",
    };

    private static readonly HashSet<string> DroppedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "iframe", "frame", "object", "embed", "link",
        "meta", "base", "form", "input", "textarea", "select", "option", "button",
        "video", "audio", "source", "track", "svg", "math", "details", "dialog", "template",
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = new(StringComparer.OrdinalIgnoreCase) { "href", "title", "name" },
        ["img"] = new(StringComparer.OrdinalIgnoreCase) { "src", "alt", "width", "height", "loading", "referrerpolicy" },
        ["td"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
        ["th"] = new(StringComparer.OrdinalIgnoreCase) { "colspan", "rowspan" },
        ["blockquote"] = new(StringComparer.OrdinalIgnoreCase) { "cite" },
        ["cite"] = new(StringComparer.OrdinalIgnoreCase) { "cite" },
        ["q"] = new(StringComparer.OrdinalIgnoreCase) { "cite" },
    };

    private static readonly HtmlParser Parser = new();

    public string Sanitize(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = Parser.ParseDocument(html);
        var body = document.Body ?? document.DocumentElement;
        if (body is null)
        {
            return string.Empty;
        }

        DropUnsafeElements(body);
        UnwrapUnknownElements(body);
        CleanAttributes(body);

        using var writer = new System.IO.StringWriter();
        body.ToHtml(writer, new HtmlMarkupFormatter());
        return writer.ToString();
    }

    private static void DropUnsafeElements(IElement root)
    {
        foreach (var element in root.QuerySelectorAll(string.Join(",", DroppedTags)))
        {
            element.Remove();
        }
    }

    private static void UnwrapUnknownElements(IElement root)
    {
        List<IElement> candidates;
        do
        {
            candidates = root.QuerySelectorAll("*")
                .Where(e => !AllowedTags.Contains(e.LocalName) && !DroppedTags.Contains(e.LocalName))
                .ToList();
            foreach (var element in candidates)
            {
                var parent = element.Parent;
                if (parent is null)
                {
                    element.Remove();
                    continue;
                }

                while (element.FirstChild is not null)
                {
                    parent.AppendChild(element.FirstChild);
                }

                element.Remove();
            }
        }
        while (candidates.Count > 0);
    }

    private void CleanAttributes(IElement root)
    {
        foreach (var element in root.QuerySelectorAll("*"))
        {
            if (!AllowedTags.Contains(element.LocalName))
            {
                continue;
            }

            var allowed = AllowedAttributes.GetValueOrDefault(element.LocalName) ?? EmptyAttributes;
            var removable = element.Attributes
                .Select(a => a.Name)
                .Where(name => !allowed.Contains(name))
                .ToList();
            foreach (var name in removable)
            {
                element.RemoveAttribute(name);
            }

            switch (element.LocalName)
            {
                case "a":
                    if (element.GetAttribute("href") is { Length: > 0 } href && !IsSafeLink(href))
                    {
                        element.RemoveAttribute("href");
                    }

                    element.SetAttribute("rel", "nofollow noopener noreferrer");
                    break;

                case "img":
                    if (element.GetAttribute("src") is { Length: > 0 } src)
                    {
                        element.SetAttribute("src", RewriteImage(src));
                        element.SetAttribute("loading", "lazy");
                        element.SetAttribute("referrerpolicy", "no-referrer");
                    }

                    break;
            }
        }
    }

    private static bool IsSafeLink(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "http" or "https" or "mailto";
    }

    private string RewriteImage(string src)
    {
        if (string.IsNullOrWhiteSpace(options?.ImageProxyBaseUrl))
        {
            return src;
        }

        if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return src;
        }

        if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return src;
        }

        var proxy = options!.ImageProxyBaseUrl!.TrimEnd('/');
        return $"{proxy}?url={Uri.EscapeDataString(src)}";
    }

    private static readonly HashSet<string> EmptyAttributes = new(StringComparer.OrdinalIgnoreCase);
}
