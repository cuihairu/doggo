using Doggo.Scraper.News;

namespace Doggo.Scraper.Tests;

public class HtmlSanitizerTests
{
    private readonly HtmlSanitizer _sanitizer = new();

    [Fact]
    public void Empty_input_returns_empty()
    {
        Assert.Equal(string.Empty, _sanitizer.Sanitize(""));
        Assert.Equal(string.Empty, _sanitizer.Sanitize("   "));
    }

    [Fact]
    public void Script_and_style_are_removed()
    {
        var result = _sanitizer.Sanitize("<p>ok</p><script>alert(1)</script><style>.x{color:red}</style>");

        Assert.Contains("<p>ok</p>", result);
        Assert.DoesNotContain("<script", result);
        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("<style", result);
    }

    [Fact]
    public void Iframe_and_embed_are_removed()
    {
        var result = _sanitizer.Sanitize("<p>a</p><iframe src=\"https://evil\"></iframe><object></object>");

        Assert.DoesNotContain("iframe", result);
        Assert.DoesNotContain("object", result);
        Assert.Contains("<p>a</p>", result);
    }

    [Fact]
    public void Unknown_elements_are_unwrapped_content_is_kept()
    {
        var result = _sanitizer.Sanitize("<article><div><p>a</p></div><section><b>b</b></section></article>");

        Assert.Contains("<p>a</p>", result);
        Assert.Contains("<b>b</b>", result);
        Assert.DoesNotContain("<div", result);
        Assert.DoesNotContain("<section", result);
        Assert.DoesNotContain("<article", result);
    }

    [Fact]
    public void Unsafe_href_removed_safe_href_gets_rel()
    {
        var result = _sanitizer.Sanitize(
            "<a href=\"javascript:alert(1)\">bad</a><a href=\"https://example.com/x\">good</a>");

        Assert.DoesNotContain("javascript", result);
        Assert.Contains("href=\"https://example.com/x\"", result);
        Assert.Contains("rel=\"nofollow noopener noreferrer\"", result);
    }

    [Fact]
    public void Event_handler_attributes_are_removed()
    {
        var result = _sanitizer.Sanitize("<p onclick=\"evil()\" data-x=\"y\">t</p>");

        Assert.DoesNotContain("onclick", result);
        Assert.DoesNotContain("data-x", result);
        Assert.Contains("<p>t</p>", result);
    }

    [Fact]
    public void Images_get_lazy_loading_and_no_referrer()
    {
        var result = _sanitizer.Sanitize("<p><img src=\"https://cdn.example.com/a.png\" alt=\"a\"></p>");

        Assert.Contains("loading=\"lazy\"", result);
        Assert.Contains("referrerpolicy=\"no-referrer\"", result);
        Assert.Contains("https://cdn.example.com/a.png", result);
    }

    [Fact]
    public void Image_proxy_rewrites_remote_src_when_configured()
    {
        var sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions { ImageProxyBaseUrl = "https://img.doggo.dev" });

        var result = sanitizer.Sanitize("<img src=\"https://cdn.example.com/a.png\" alt=\"a\">");

        Assert.Contains("https://img.doggo.dev?url=" + Uri.EscapeDataString("https://cdn.example.com/a.png"), result);
    }

    [Fact]
    public void Table_and_code_structures_are_preserved()
    {
        var result = _sanitizer.Sanitize(
            "<table><tr><td colspan=\"2\">c</td></tr></table><pre><code>var x = 1;</code></pre>");

        Assert.Contains("<table>", result);
        Assert.Contains("colspan=\"2\"", result);
        Assert.Contains("<pre><code>", result);
    }
}
