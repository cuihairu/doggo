using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class NewsApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(Guid SourceId, Guid ItemId)> SeedAsync(params NewsItem[] items)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var source = new NewsSource
        {
            Id = Guid.CreateVersion7(),
            Name = "seed-source",
            SourceType = NewsSourceType.Rss,
            Game = GameKind.Cs2,
            Url = "https://example.com/feed",
        };
        db.NewsSources.Add(source);
        foreach (var item in items)
        {
            item.Id = Guid.CreateVersion7();
            item.SourceId = source.Id;
            item.UrlHash = Hash(item.Url);
            db.NewsItems.Add(item);
        }

        await db.SaveChangesAsync();
        return (source.Id, items[0].Id);
    }

    private static string Hash(string url) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    [Fact]
    public async Task News_list_returns_seeded_items()
    {
        var id = await SeedAsync(new NewsItem
        {
            Title = "CS2 update shipped",
            Url = "https://example.com/a",
            Game = GameKind.Cs2,
            Category = "update",
            PublishedAtUtc = new DateTime(2025, 10, 9, 0, 0, 0, DateTimeKind.Utc),
        });

        var response = await Factory.CreateClient().GetAsync("/api/news?page=1&pageSize=10");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal("CS2 update shipped", body.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal("seed-source", body.GetProperty("items")[0].GetProperty("sourceName").GetString());
    }

    [Fact]
    public async Task News_list_filters_by_game_and_category()
    {
        await SeedAsync(
            new NewsItem
            {
                Title = "CS item",
                Url = "https://example.com/cs",
                Game = GameKind.Cs2,
                Category = "update",
                PublishedAtUtc = DateTime.UtcNow,
            },
            new NewsItem
            {
                Title = "Dota item",
                Url = "https://example.com/dota",
                Game = GameKind.Dota2,
                Category = "news",
                PublishedAtUtc = DateTime.UtcNow,
            });

        var client = Factory.CreateClient();
        var byGame = await client.GetFromJsonAsync<JsonElement>("/api/news?game=Dota2");
        var byCategory = await client.GetFromJsonAsync<JsonElement>("/api/news?category=update");

        Assert.Equal(1, byGame.GetProperty("total").GetInt32());
        Assert.Equal("Dota item", byGame.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(1, byCategory.GetProperty("total").GetInt32());
        Assert.Equal("CS item", byCategory.GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task News_detail_increments_view_count()
    {
        var (_, itemId) = await SeedAsync(new NewsItem
        {
            Title = "Detail target",
            Url = "https://example.com/detail",
            Game = GameKind.Cs2,
            Category = "news",
            PublishedAtUtc = DateTime.UtcNow,
        });

        var client = Factory.CreateClient();
        var first = await client.GetFromJsonAsync<JsonElement>($"/api/news/{itemId}");
        var second = await client.GetFromJsonAsync<JsonElement>($"/api/news/{itemId}");

        Assert.Equal(1, first.GetProperty("meta").GetProperty("viewCount").GetInt64());
        Assert.Equal(2, second.GetProperty("meta").GetProperty("viewCount").GetInt64());
    }

    [Fact]
    public async Task News_detail_of_unknown_id_is_404()
    {
        var response = await Factory.CreateClient().GetAsync($"/api/news/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class ContentApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Notices_only_active_window_is_returned()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.Notices.AddRange(
            new Notice { Id = Guid.CreateVersion7(), Title = "Active", Content = "hello", Enabled = true },
            new Notice { Id = Guid.CreateVersion7(), Title = "Expired", Content = "old", Enabled = true, EndAtUtc = DateTime.UtcNow.AddDays(-1) },
            new Notice { Id = Guid.CreateVersion7(), Title = "Disabled", Content = "no", Enabled = false });
        await db.SaveChangesAsync();

        var body = await Factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/notices");

        Assert.Equal(1, body.GetArrayLength());
        Assert.Equal("Active", body[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Banners_filter_by_position_and_order()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.Banners.AddRange(
            new Banner { Id = Guid.CreateVersion7(), Title = "Home 2", ImageUrl = "https://img/2", LinkUrl = "https://x/2", Position = "home", SortOrder = 2 },
            new Banner { Id = Guid.CreateVersion7(), Title = "Home 1", ImageUrl = "https://img/1", LinkUrl = "https://x/1", Position = "home", SortOrder = 1 },
            new Banner { Id = Guid.CreateVersion7(), Title = "Side", ImageUrl = "https://img/s", LinkUrl = "https://x/s", Position = "side", SortOrder = 0 });
        await db.SaveChangesAsync();

        var body = await Factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/banners?position=home");

        Assert.Equal(2, body.GetArrayLength());
        Assert.Equal("Home 1", body[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Article_detail_counts_view_and_404_when_disabled()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var enabled = new Article { Id = Guid.CreateVersion7(), Title = "Guide", ContentHtml = "<p>text</p>", Enabled = true };
        var disabled = new Article { Id = Guid.CreateVersion7(), Title = "Hidden", ContentHtml = "<p>x</p>", Enabled = false };
        db.Articles.AddRange(enabled, disabled);
        await db.SaveChangesAsync();

        var client = Factory.CreateClient();
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{enabled.Id}");
        var missing = await client.GetAsync($"/api/articles/{disabled.Id}");

        Assert.Equal("Guide", body.GetProperty("title").GetString());
        Assert.Equal(1, body.GetProperty("viewCount").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
