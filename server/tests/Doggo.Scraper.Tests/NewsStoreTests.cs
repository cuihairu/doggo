using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Doggo.Scraper.News;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Scraper.Tests;

public class NewsStoreTests
{
    private static DoggoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<NewsSource> CreateSourceAsync(DoggoDbContext db)
    {
        var source = new NewsSource
        {
            Name = "test-source",
            SourceType = NewsSourceType.Rss,
            Game = GameKind.Cs2,
            Url = "https://example.com/feed",
        };
        db.NewsSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    [Fact]
    public async Task New_items_are_inserted_and_deduped_by_url()
    {
        await using var db = CreateContext();
        var source = await CreateSourceAsync(db);
        var store = new NewsStore(db, new HtmlSanitizer());
        var candidates = new[]
        {
            new NewsCandidate("Post A", "https://example.com/a", DateTime.UtcNow, "news"),
            new NewsCandidate("Post B", "https://example.com/b", DateTime.UtcNow, "news"),
        };

        var inserted = await store.SaveCandidatesAsync(source, candidates);
        var again = await store.SaveCandidatesAsync(source, candidates);
        var withTrackingParams = await store.SaveCandidatesAsync(source,
            [new NewsCandidate("Post A (share)", "https://example.com/a?utm_source=tg", DateTime.UtcNow, "news")]);

        Assert.Equal(2, inserted);
        Assert.Equal(0, again);
        Assert.Equal(0, withTrackingParams);
        Assert.Equal(2, await db.NewsItems.CountAsync());
    }

    [Fact]
    public async Task Content_is_sanitized_and_fields_are_truncated()
    {
        await using var db = CreateContext();
        var source = await CreateSourceAsync(db);
        var store = new NewsStore(db, new HtmlSanitizer());
        var longTitle = string.Concat(Enumerable.Repeat("very long title ", 30));
        var candidates = new[]
        {
            new NewsCandidate(
                longTitle,
                "https://example.com/dirty",
                DateTime.UtcNow,
                "news",
                Summary: "<p>summary</p>",
                ContentHtml: "<div><p>kept</p><script>evil()</script></div>"),
        };

        await store.SaveCandidatesAsync(source, candidates);

        var item = await db.NewsItems.SingleAsync();
        Assert.Equal(255, item.Title.Length);
        Assert.Contains("<p>kept</p>", item.ContentHtml);
        Assert.DoesNotContain("script", item.ContentHtml);
        Assert.StartsWith("https://example.com/dirty", item.Url);
    }

    [Fact]
    public async Task Source_bookkeeping_and_cursor_are_updated()
    {
        await using var db = CreateContext();
        var source = await CreateSourceAsync(db);
        source.ConsecutiveFailures = 3;
        await db.SaveChangesAsync();
        var store = new NewsStore(db, new HtmlSanitizer());

        await store.SaveCandidatesAsync(source,
            [new NewsCandidate("Post", "https://example.com/p1", new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc), "news")]);

        Assert.NotNull(source.LastFetchedAtUtc);
        Assert.Equal(0, source.ConsecutiveFailures);
        var cursor = await db.SyncCursors.SingleAsync(c => c.SourceName == source.Name);
        Assert.Contains("2025-10-01", cursor.Cursor);
    }

    [Fact]
    public async Task Url_hash_is_stable_and_hex()
    {
        await using var db = CreateContext();
        var source = await CreateSourceAsync(db);
        var store = new NewsStore(db, new HtmlSanitizer());

        await store.SaveCandidatesAsync(source,
            [new NewsCandidate("Post", "https://EXAMPLE.com/Post/", DateTime.UtcNow, "news")]);

        var item = await db.NewsItems.SingleAsync();
        Assert.Equal(NewsFingerprint.ComputeUrlHash("https://example.com/Post"), item.UrlHash);
        Assert.Equal("https://example.com/Post", item.Url);
    }
}
