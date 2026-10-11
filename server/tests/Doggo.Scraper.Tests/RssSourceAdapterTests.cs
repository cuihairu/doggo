using Doggo.Scraper.News.Adapters;

namespace Doggo.Scraper.Tests;

public class RssSourceAdapterTests
{
    private const string RssFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:content="http://purl.org/rss/1.0/modules/content/" xmlns:media="http://search.yahoo.com/mrss/">
          <channel>
            <title>CS Blog</title>
            <link>https://blog.example.com</link>
            <item>
              <title>Counter-Strike 2 Update</title>
              <link>https://blog.example.com/news/cs2-update</link>
              <pubDate>Thu, 09 Oct 2025 12:00:00 GMT</pubDate>
              <dc:creator>Valve</dc:creator>
              <category>update</category>
              <category>patchnotes</category>
              <description>Short summary</description>
              <content:encoded><![CDATA[<p>Full <b>body</b> text</p>]]></content:encoded>
              <enclosure url="https://cdn.example.com/cover.jpg" type="image/jpeg" length="1"/>
            </item>
            <item>
              <title>Second post</title>
              <link>https://blog.example.com/news/second?utm_source=rss</link>
              <pubDate>Wed, 08 Oct 2025 10:30:00 +0200</pubDate>
            </item>
          </channel>
        </rss>
        """;

    private const string AtomFeed = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Esports</title>
          <entry>
            <title>Major announced</title>
            <link rel="alternate" href="https://esports.example.com/major"/>
            <updated>2025-10-01T08:00:00Z</updated>
            <author><name>News Desk</name></author>
            <summary>Summary text</summary>
            <content type="html">&lt;p&gt;Body&lt;/p&gt;</content>
          </entry>
        </feed>
        """;

    [Fact]
    public void Rss_items_are_parsed()
    {
        var items = RssSourceAdapter.Parse(RssFeed);

        Assert.Equal(2, items.Count);
        Assert.Equal("Counter-Strike 2 Update", items[0].Title);
        Assert.Equal("https://blog.example.com/news/cs2-update", items[0].Url);
        Assert.Equal("Valve", items[0].Author);
        Assert.Equal("update", items[0].Category);
        Assert.Equal("update,patchnotes", items[0].Tags);
        Assert.Equal("Short summary", items[0].Summary);
        Assert.Contains("<b>body</b>", items[0].ContentHtml);
        Assert.Equal("https://cdn.example.com/cover.jpg", items[0].CoverUrl);
        Assert.Equal(new DateTime(2025, 10, 9, 12, 0, 0, DateTimeKind.Utc), items[0].PublishedAtUtc);
    }

    [Fact]
    public void Rss_dates_are_normalized_to_utc()
    {
        var items = RssSourceAdapter.Parse(RssFeed);

        Assert.Equal(new DateTime(2025, 10, 8, 8, 30, 0, DateTimeKind.Utc), items[1].PublishedAtUtc);
    }

    [Fact]
    public void Rss_item_without_title_is_skipped()
    {
        var feed = """
            <rss version="2.0"><channel>
              <item><link>https://a.example/1</link></item>
            </channel></rss>
            """;

        Assert.Empty(RssSourceAdapter.Parse(feed));
    }

    [Fact]
    public void Invalid_xml_returns_empty()
    {
        Assert.Empty(RssSourceAdapter.Parse("not xml at all"));
    }

    [Fact]
    public void Atom_entries_are_parsed()
    {
        var items = RssSourceAdapter.Parse(AtomFeed);

        var item = Assert.Single(items);
        Assert.Equal("Major announced", item.Title);
        Assert.Equal("https://esports.example.com/major", item.Url);
        Assert.Equal("News Desk", item.Author);
        Assert.Equal("Summary text", item.Summary);
        Assert.Contains("Body", item.ContentHtml);
        Assert.Equal(new DateTime(2025, 10, 1, 8, 0, 0, DateTimeKind.Utc), item.PublishedAtUtc);
    }
}
