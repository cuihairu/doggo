using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Price;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>The /api/admin/price-crawl surface: map review, series query, spread report.</summary>
public class PriceCrawlApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private async Task<HttpClient> AdminClientAsync(string account)
    {
        var client = Factory.CreateClient();
        var register = await client.PostAsync("/api/auth/register", new StringContent(
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

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path} -> {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<PriceCrawlItemMap> SeedMapAsync(string externalId, PriceMapStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var map = new PriceCrawlItemMap
        {
            Id = Guid.CreateVersion7(),
            SiteKey = "test-site",
            ExternalId = externalId,
            ExternalTitle = $"Item {externalId}",
            ExternalUrl = $"https://test.example/item/{externalId}",
            MarketItemId = Guid.CreateVersion7(),
            MapStatus = status,
            MatchSource = PriceMapSource.Auto
        };
        db.PriceCrawlItemMaps.Add(map);
        await db.SaveChangesAsync();
        return map;
    }

    private async Task SeedSeriesAsync(Guid mapId, Guid marketItemId, long cents, DateTime observedAtUtc)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.PriceCrawlSeriesPoints.Add(new PriceCrawlSeries
        {
            Id = Guid.CreateVersion7(),
            MapId = mapId,
            MarketItemId = marketItemId,
            SiteKey = "test-site",
            ExternalId = "ext-1",
            PriceCents = cents,
            CurrencyCode = "CNY",
            ConvertedCents = cents,
            ExchangeRate = 1m,
            ObservedAtUtc = observedAtUtc,
            WindowKey = observedAtUtc.ToString("yyyyMMddHHmm")
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Price_crawl_surface_requires_the_admin_role()
    {
        // Authenticated but not flagged: the Admin policy rejects with 403,
        // the same gate AdminApiTests exercises for /api/admin.
        var account = "13900000030";
        var register = await Factory.CreateClient().PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();

        var client = Factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());

        var forbidden = await client.GetAsync("/api/admin/price-crawl/maps");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Maps_list_filter_confirm_and_reject()
    {
        var client = await AdminClientAsync("13900000031");
        var unconfirmed = await SeedMapAsync("ext-u", PriceMapStatus.Unconfirmed);
        var confirmed = await SeedMapAsync("ext-c", PriceMapStatus.Confirmed);

        var list = await GetJsonAsync(client, "/api/admin/price-crawl/maps?take=10");
        Assert.True(list.RootElement.GetArrayLength() >= 2);

        var filtered = await GetJsonAsync(client, "/api/admin/price-crawl/maps?status=Unconfirmed");
        Assert.All(filtered.RootElement.EnumerateArray(), e =>
            Assert.Equal((int)PriceMapStatus.Unconfirmed, e.GetProperty("mapStatus").GetInt32()));

        var reject = await client.PostAsync($"/api/admin/price-crawl/maps/{unconfirmed.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var confirm = await client.PostAsync($"/api/admin/price-crawl/maps/{confirmed.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using var db = Factory.Services.CreateScope().ServiceProvider
            .GetRequiredService<DoggoDbContext>();
        Assert.Equal(PriceMapStatus.Rejected, (await db.PriceCrawlItemMaps.SingleAsync(m => m.Id == unconfirmed.Id)).MapStatus);
        Assert.Equal(PriceMapSource.Manual, (await db.PriceCrawlItemMaps.SingleAsync(m => m.Id == confirmed.Id)).MatchSource);
    }

    [Fact]
    public async Task Confirm_unknown_map_returns_404()
    {
        var client = await AdminClientAsync("13900000032");
        var response = await client.PostAsync($"/api/admin/price-crawl/maps/{Guid.CreateVersion7()}/confirm", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Series_query_returns_points_daily_trend_and_period_change()
    {
        var client = await AdminClientAsync("13900000033");
        var map = await SeedMapAsync("ext-s", PriceMapStatus.Confirmed);
        // Fixed days: relative hours would land both points on the same UTC day near midnight.
        await SeedSeriesAsync(map.Id, map.MarketItemId!.Value, 10000, new DateTime(2026, 10, 11, 10, 0, 0, DateTimeKind.Utc));
        await SeedSeriesAsync(map.Id, map.MarketItemId!.Value, 11000, new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc));

        using var doc = await GetJsonAsync(client, $"/api/admin/price-crawl/prices/{map.MarketItemId.Value}");
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("points").GetArrayLength());
        Assert.Equal(2, root.GetProperty("daily").GetArrayLength());
        Assert.Equal(2, root.GetProperty("trend").GetArrayLength());

        var latestTrend = root.GetProperty("trend").EnumerateArray().Last();
        Assert.Equal(11000, latestTrend.GetProperty("closeCents").GetInt64());
        Assert.Equal(10, latestTrend.GetProperty("dayOverDayPct").GetDecimal());
        Assert.Equal(10, root.GetProperty("periodChange7dPct").GetDecimal());
    }

    [Fact]
    public async Task Spread_report_aggregates_converted_cents()
    {
        var client = await AdminClientAsync("13900000034");
        var mapA = await SeedMapAsync("ext-a", PriceMapStatus.Confirmed);
        var itemId = mapA.MarketItemId!.Value;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            // Dynamic times inside the 7d report window (fixed dates would age out); the two
            // instants differ by 22h so the unique (map, window) keys never collide.
            var t1 = DateTime.UtcNow.AddDays(-1);
            var t2 = DateTime.UtcNow.AddHours(-2);
            db.PriceCrawlSeriesPoints.Add(new PriceCrawlSeries
            {
                Id = Guid.CreateVersion7(),
                MapId = mapA.Id,
                MarketItemId = itemId,
                SiteKey = "site-1",
                ExternalId = "ext-a",
                PriceCents = 10000,
                CurrencyCode = "CNY",
                ConvertedCents = 10000,
                ObservedAtUtc = t1,
                WindowKey = t1.ToString("yyyyMMddHHmm"),
                IsSuspect = false
            });
            db.PriceCrawlSeriesPoints.Add(new PriceCrawlSeries
            {
                Id = Guid.CreateVersion7(),
                MapId = mapA.Id,
                MarketItemId = itemId,
                SiteKey = "site-2",
                ExternalId = "ext-a2",
                PriceCents = 12000,
                CurrencyCode = "CNY",
                ConvertedCents = 12000,
                ObservedAtUtc = t2,
                WindowKey = t2.ToString("yyyyMMddHHmm"),
                IsSuspect = false
            });
            await db.SaveChangesAsync();
        }

        using var doc = await GetJsonAsync(client, "/api/admin/price-crawl/spread-report?days=7");
        var row = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(itemId.ToString(), row.GetProperty("marketItemId").GetString());
        Assert.Equal(2, row.GetProperty("siteCount").GetInt32());
        Assert.Equal(10000, row.GetProperty("minCents").GetInt64());
        Assert.Equal(12000, row.GetProperty("maxCents").GetInt64());
        Assert.Equal(2000, row.GetProperty("spreadCents").GetInt64());
        Assert.Equal(20, row.GetProperty("spreadPct").GetDecimal());
    }
}
