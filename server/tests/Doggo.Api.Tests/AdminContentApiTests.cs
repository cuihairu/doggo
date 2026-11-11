using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Domain.News;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>The /api/admin content surface: articles, banners, notices, promotions, sources.</summary>
public class AdminContentApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private async Task<HttpClient> AdminClientAsync()
    {
        var account = $"138{Random.Shared.Next(100000, 999999):D6}";
        var register = await Factory.CreateClient().PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var user = await db.UserAccounts.SingleAsync(u => u.Phone == account);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task SeedAsync(params object[] entities)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    // ---- articles ----

    [Fact]
    public async Task Article_lifecycle_create_update_delete_runs_through_the_admin_api()
    {
        var client = await AdminClientAsync();

        var created = await client.PostAsJsonAsync("/api/admin/articles", new
        {
            title = "Patch notes",
            summary = "Summary line",
            contentHtml = "<p>hello</p>",
            keywords = "cs2,patch",
            category = "news"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var article = await JsonAsync(created);
        var articleId = article.GetProperty("id").GetGuid();
        Assert.Equal("Patch notes", article.GetProperty("title").GetString());

        var list = await JsonAsync(await client.GetAsync("/api/admin/articles"));
        Assert.Contains(list.EnumerateArray(), a => a.GetProperty("id").GetGuid() == articleId);

        var updated = await client.PutAsJsonAsync($"/api/admin/articles/{articleId}", new
        {
            title = "Patch notes v2",
            summary = "Rewritten",
            contentHtml = "<p>v2</p>",
            keywords = "cs2",
            category = "news"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var afterUpdate = await JsonAsync(updated);
        Assert.Equal("Patch notes v2", afterUpdate.GetProperty("title").GetString());
        Assert.Equal("Rewritten", afterUpdate.GetProperty("summary").GetString());

        var deleted = await client.DeleteAsync($"/api/admin/articles/{articleId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            Assert.False(await db.Articles.AnyAsync(a => a.Id == articleId));
        }
    }

    [Fact]
    public async Task Article_update_and_delete_reject_unknown_ids_with_404()
    {
        var client = await AdminClientAsync();
        var missing = $"/api/admin/articles/{Guid.CreateVersion7()}";

        var put = await client.PutAsJsonAsync(missing, new
        {
            title = "t",
            summary = "s",
            contentHtml = "<p>x</p>",
            keywords = "k",
            category = "c"
        });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        var delete = await client.DeleteAsync(missing);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    // ---- banners ----

    [Fact]
    public async Task Banner_delete_disables_the_banner_and_rejects_unknown_ids()
    {
        var client = await AdminClientAsync();
        var bannerId = Guid.CreateVersion7();
        await SeedAsync(new Banner
        {
            Id = bannerId,
            Title = "Summer sale",
            ImageUrl = "https://cdn.test/banner.png",
            LinkUrl = "https://test.test/sale",
            Position = "home-top",
            SortOrder = 1,
            Enabled = true
        });

        var missing = await client.DeleteAsync($"/api/admin/banners/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var deleted = await client.DeleteAsync($"/api/admin/banners/{bannerId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var banner = await db.Banners.SingleAsync(b => b.Id == bannerId);
            Assert.False(banner.Enabled);
        }
    }

    // ---- notices ----

    [Fact]
    public async Task Notice_create_then_delete_and_404_on_unknown_id()
    {
        var client = await AdminClientAsync();

        var created = await client.PostAsJsonAsync("/api/admin/notices", new
        {
            title = "Maintenance",
            content = "Downtime tonight",
            startAtUtc = (DateTime?)null,
            endAtUtc = (DateTime?)null
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var noticeId = (await JsonAsync(created)).GetProperty("id").GetGuid();

        var missing = await client.DeleteAsync($"/api/admin/notices/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var deleted = await client.DeleteAsync($"/api/admin/notices/{noticeId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    // ---- promotions ----

    [Fact]
    public async Task Promotion_enabled_flip_works_and_unknown_id_returns_404()
    {
        var client = await AdminClientAsync();
        var planId = Guid.CreateVersion7();
        await SeedAsync(new PromotionPlan
        {
            Id = planId,
            ProductId = Guid.CreateVersion7(),
            DiscountPriceCents = 990,
            Stock = 10,
            PerUserLimit = 1,
            Enabled = true
        });

        var missing = await client.PutAsJsonAsync($"/api/admin/promotions/{Guid.CreateVersion7()}/enabled", new { enabled = true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var flipped = await client.PutAsJsonAsync($"/api/admin/promotions/{planId}/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, flipped.StatusCode);
        var body = await JsonAsync(flipped);
        Assert.False(body.GetProperty("enabled").GetBoolean());

        var list = await JsonAsync(await client.GetAsync("/api/admin/promotions"));
        Assert.Contains(list.EnumerateArray(), p => p.GetProperty("id").GetGuid() == planId);
    }

    // ---- news sources / items ----

    [Fact]
    public async Task News_source_enabled_flip_works_and_unknown_id_returns_404()
    {
        var client = await AdminClientAsync();
        var sourceId = Guid.CreateVersion7();
        await SeedAsync(new NewsSource
        {
            Id = sourceId,
            Name = "CS2 Blog",
            SourceType = NewsSourceType.Rss,
            Game = GameKind.Cs2,
            Url = "https://blog.test/rss",
            Enabled = true
        });

        var missing = await client.PutAsJsonAsync($"/api/admin/news-sources/{Guid.CreateVersion7()}/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var flipped = await client.PutAsJsonAsync($"/api/admin/news-sources/{sourceId}/enabled", new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, flipped.StatusCode);
        Assert.False((await JsonAsync(flipped)).GetProperty("enabled").GetBoolean());

        var list = await JsonAsync(await client.GetAsync("/api/admin/news-sources"));
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("id").GetGuid() == sourceId);
    }

    [Fact]
    public async Task News_items_return_a_paged_envelope()
    {
        var client = await AdminClientAsync();
        var now = DateTime.UtcNow;
        await SeedAsync(
            new NewsItem { Id = Guid.CreateVersion7(), Title = "A", PublishedAtUtc = now.AddMinutes(-1) },
            new NewsItem { Id = Guid.CreateVersion7(), Title = "B", PublishedAtUtc = now.AddMinutes(-2) },
            new NewsItem { Id = Guid.CreateVersion7(), Title = "C", PublishedAtUtc = now.AddMinutes(-3) });

        var page = await JsonAsync(await client.GetAsync("/api/admin/news-items?page=2&pageSize=1"));
        Assert.Equal(3, page.GetProperty("total").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
    }
}
