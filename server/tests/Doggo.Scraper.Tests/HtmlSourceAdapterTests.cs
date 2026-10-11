using Doggo.Scraper.News;
using Doggo.Scraper.News.Adapters;

namespace Doggo.Scraper.Tests;

public sealed class StubPageFetcher(string html) : IPageFetcher
{
    public Task<string> FetchAsync(string url, CancellationToken cancellationToken) => Task.FromResult(html);
}

public class HtmlSourceAdapterTests
{
    private const string Page = """
        <html><body>
          <article class="post">
            <h2><a href="/news/first">First post</a></h2>
            <time datetime="2025-10-09T08:00:00Z">Oct 9</time>
            <div class="summary">Short summary</div>
            <div class="body">Body <b>text</b></div>
          </article>
          <article class="post">
            <h2><a href="https://other.example.com/news/second">Second post</a></h2>
          </article>
        </body></html>
        """;

    private static readonly Uri BaseUri = new("https://news.example.com/index");

    [Fact]
    public void Items_are_extracted_by_selectors()
    {
        var options = new HtmlAdapterOptions
        {
            ItemSelector = "article",
            TitleSelector = "h2 a",
            LinkSelector = "h2 a",
            DateSelector = "time",
            SummarySelector = ".summary",
            ContentSelector = ".body",
            Category = "news",
        };

        var items = HtmlSourceAdapter.Parse(Page, BaseUri, options);

        Assert.Equal(2, items.Count);
        Assert.Equal("First post", items[0].Title);
        Assert.Equal("https://news.example.com/news/first", items[0].Url);
        Assert.Equal("Short summary", items[0].Summary);
        Assert.Contains("<b>text</b>", items[0].ContentHtml);
        Assert.Equal(new DateTime(2025, 10, 9, 8, 0, 0, DateTimeKind.Utc), items[0].PublishedAtUtc);
        Assert.Equal("news", items[0].Category);
        Assert.Equal("Second post", items[1].Title);
        Assert.Equal("https://other.example.com/news/second", items[1].Url);
        Assert.NotEqual(DateTime.MinValue, items[1].PublishedAtUtc);
    }

    [Fact]
    public void Items_without_links_are_skipped()
    {
        var options = new HtmlAdapterOptions { ItemSelector = "article", TitleSelector = "h2 a", LinkSelector = ".missing" };

        Assert.Empty(HtmlSourceAdapter.Parse(Page, BaseUri, options));
    }

    [Fact]
    public async Task Adapter_reads_source_configuration()
    {
        var fetcher = new StubPageFetcher(Page);
        var source = new Doggo.Domain.News.NewsSource
        {
            Name = "html-src",
            SourceType = Doggo.Domain.News.NewsSourceType.Html,
            Url = "https://news.example.com/index",
            ConfigJson = """{"itemSelector":"article","linkSelector":"h2 a","titleSelector":"h2 a"}""",
        };

        var adapter = new HtmlSourceAdapter(fetcher);
        var items = await adapter.FetchAsync(source, CancellationToken.None);

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task Missing_configuration_throws()
    {
        var adapter = new HtmlSourceAdapter(new StubPageFetcher(Page));
        var source = new Doggo.Domain.News.NewsSource
        {
            Name = "html-src",
            SourceType = Doggo.Domain.News.NewsSourceType.Html,
            Url = "https://news.example.com/index",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.FetchAsync(source, CancellationToken.None));
    }
}
