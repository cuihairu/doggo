using System.Text.Json;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Price;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class MarketApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<MarketItem> SeedAsync(decimal priceCents = 1500, GameKind game = GameKind.Cs2, string hashName = "Snakebite Case")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var item = new MarketItem
        {
            // Pre-assign: ValueGeneratedNever leaves adds on a temporary default key otherwise.
            Id = Guid.CreateVersion7(),
            ClassId = "493",
            MarketHashName = hashName,
            ItemType = MarketItemType.Other,
            Game = game,
            IconUrl = ""
        };
        db.MarketItems.Add(item);
        db.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            MarketItemId = item.Id,
            PriceCents = (long)priceCents,
            Currency = "CNY",
            CapturedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.MarketPriceDailies.Add(new MarketPriceDaily
        {
            MarketItemId = item.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            OpenCents = 1400,
            HighCents = 1600,
            LowCents = 1350,
            CloseCents = (long)priceCents
        });
        await db.SaveChangesAsync();
        return item;
    }

    private async Task SeedRatesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.ExchangeRates.Add(new Doggo.Domain.Products.ExchangeRate
        {
            CurrencyCode = "USD",
            Rate = 7.15m,
            Source = "manual"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_returns_items_with_latest_price()
    {
        var seeded = await SeedAsync(priceCents: 1500);

        var client = Factory.CreateClient();
        var json = await client.GetStringAsync("/api/market/items");
        using var document = JsonDocument.Parse(json);

        Assert.Equal(1, document.RootElement.GetProperty("total").GetInt32());
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(seeded.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("Snakebite Case", item.GetProperty("marketHashName").GetString());
        Assert.Equal(1500, item.GetProperty("latestPriceCents").GetInt64());
        Assert.Equal("CNY", item.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task List_reports_day_change_from_the_two_latest_daily_closes()
    {
        var seeded = await SeedAsync(priceCents: 1500);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            db.MarketPriceDailies.Add(new MarketPriceDaily
            {
                MarketItemId = seeded.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                OpenCents = 1200,
                HighCents = 1550,
                LowCents = 1200,
                CloseCents = 1200
            });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/market/items"));
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();

        // 1200 -> 1500 is +300 cents, +25% against the previous close.
        Assert.Equal(300, item.GetProperty("changeCents").GetInt64());
        Assert.Equal(2500, item.GetProperty("changeBps").GetInt32());
    }

    [Fact]
    public async Task List_leaves_change_null_without_a_previous_close()
    {
        var seeded = await SeedAsync(priceCents: 1500);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            // Yesterday's row only: no close to compare against.
            db.MarketPriceDailies.RemoveRange(await db.MarketPriceDailies.Where(d => d.MarketItemId == seeded.Id).ToListAsync());
            db.MarketPriceDailies.Add(new MarketPriceDaily
            {
                MarketItemId = seeded.Id,
                Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                OpenCents = 1200,
                HighCents = 1550,
                LowCents = 1200,
                CloseCents = 1200
            });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/api/market/items"));
        var item = document.RootElement.GetProperty("items").EnumerateArray().Single();

        // The listed price is still the latest snapshot; only the change is unknown.
        Assert.Equal(1500, item.GetProperty("latestPriceCents").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("changeCents").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("changeBps").ValueKind);
    }

    [Fact]
    public async Task List_search_filters_by_hash_name()
    {
        await SeedAsync(hashName: "Snakebite Case");
        await SeedAsync(priceCents: 900, game: GameKind.Dota2, hashName: "Treasure of the Dawn");

        var client = Factory.CreateClient();

        var hit = JsonDocument.Parse(await client.GetStringAsync("/api/market/items?q=snakebite"));
        Assert.Equal(1, hit.RootElement.GetProperty("total").GetInt32());

        var miss = JsonDocument.Parse(await client.GetStringAsync("/api/market/items?q=nonexistent-item"));
        Assert.Equal(0, miss.RootElement.GetProperty("total").GetInt32());

        var gameFilter = JsonDocument.Parse(await client.GetStringAsync("/api/market/items?game=Dota2"));
        Assert.Equal(1, gameFilter.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Prices_returns_daily_series()
    {
        var seeded = await SeedAsync(priceCents: 1500);

        var client = Factory.CreateClient();
        var json = await client.GetStringAsync($"/api/market/items/{seeded.Id}/prices");
        using var document = JsonDocument.Parse(json);

        var point = document.RootElement.GetProperty("points").EnumerateArray().Single();
        Assert.Equal(1400, point.GetProperty("openCents").GetInt64());
        Assert.Equal(1600, point.GetProperty("highCents").GetInt64());
        Assert.Equal(1350, point.GetProperty("lowCents").GetInt64());
        Assert.Equal(1500, point.GetProperty("closeCents").GetInt64());
    }

    [Fact]
    public async Task Prices_for_unknown_item_is_not_found()
    {
        var client = Factory.CreateClient();
        using var response = await client.GetAsync($"/api/market/items/{Guid.NewGuid()}/prices");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_rates_lists_seeded_rows()
    {
        await SeedRatesAsync();

        var client = Factory.CreateClient();
        var json = await client.GetStringAsync("/api/exchange-rates");
        using var document = JsonDocument.Parse(json);

        var rate = document.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("USD", rate.GetProperty("currencyCode").GetString());
        Assert.Equal(7.15m, rate.GetProperty("rate").GetDecimal());
        Assert.Equal("manual", rate.GetProperty("source").GetString());
    }

    /// <summary>Crawled series point for the public reference view; only Confirmed maps reach the series in production.</summary>
    private async Task SeedCrawlSeriesAsync(Guid marketItemId, string siteKey, long cents, DateTime observedAtUtc, bool isSuspect = false, bool converted = true)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.PriceCrawlSeriesPoints.Add(new PriceCrawlSeries
        {
            Id = Guid.CreateVersion7(),
            MapId = Guid.CreateVersion7(),
            MarketItemId = marketItemId,
            SiteKey = siteKey,
            ExternalId = $"ext-{siteKey}-{observedAtUtc:yyyyMMddHHmmss}",
            PriceCents = cents,
            CurrencyCode = "CNY",
            // null marks the missing-exchange-rate case the reference view must skip.
            ConvertedCents = converted ? cents : null,
            ExchangeRate = 1m,
            ObservedAtUtc = observedAtUtc,
            WindowKey = observedAtUtc.ToString("yyyyMMddHHmm"),
            IsSuspect = isSuspect
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Reference_returns_median_of_latest_per_site_with_spread()
    {
        var seeded = await SeedAsync(priceCents: 1500);
        var baseTime = DateTime.UtcNow.AddHours(-2);
        await SeedCrawlSeriesAsync(seeded.Id, "site-a", 10000, baseTime);
        await SeedCrawlSeriesAsync(seeded.Id, "site-b", 12000, baseTime.AddMinutes(10));
        await SeedCrawlSeriesAsync(seeded.Id, "site-c", 20000, baseTime.AddMinutes(20));

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/market/items/{seeded.Id}/reference"));

        // Median of 3 site values is the middle one; in-site baseline is the daily close.
        Assert.Equal(3, document.RootElement.GetProperty("siteCount").GetInt32());
        Assert.Equal(12000, document.RootElement.GetProperty("externalMedianCents").GetInt64());
        Assert.Equal(1500, document.RootElement.GetProperty("inSiteCloseCents").GetInt64());
        Assert.Equal(700, document.RootElement.GetProperty("spreadPct").GetDecimal());
    }

    [Fact]
    public async Task Reference_uses_each_sites_latest_and_skips_suspect_or_unconverted()
    {
        var seeded = await SeedAsync(priceCents: 1500);
        var baseTime = DateTime.UtcNow.AddHours(-3);
        // site-a: superseded older observation, then a suspect and an unconverted point that
        // must not displace the newest usable value (30000).
        await SeedCrawlSeriesAsync(seeded.Id, "site-a", 10000, baseTime);
        await SeedCrawlSeriesAsync(seeded.Id, "site-a", 30000, baseTime.AddMinutes(30));
        await SeedCrawlSeriesAsync(seeded.Id, "site-a", 99900, baseTime.AddMinutes(40), isSuspect: true);
        await SeedCrawlSeriesAsync(seeded.Id, "site-a", 50000, baseTime.AddMinutes(50), converted: false);
        await SeedCrawlSeriesAsync(seeded.Id, "site-b", 20000, baseTime.AddMinutes(10));

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/market/items/{seeded.Id}/reference"));

        // site-a contributes 30000 (suspect/unconverted skipped), site-b 20000; even count averages.
        Assert.Equal(2, document.RootElement.GetProperty("siteCount").GetInt32());
        Assert.Equal(25000, document.RootElement.GetProperty("externalMedianCents").GetInt64());
    }

    [Fact]
    public async Task Reference_without_external_series_returns_nulls()
    {
        var seeded = await SeedAsync(priceCents: 1500);

        var client = Factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync($"/api/market/items/{seeded.Id}/reference"));

        Assert.Equal(0, document.RootElement.GetProperty("siteCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("externalMedianCents").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("externalObservedAtUtc").ValueKind);
        Assert.Equal(1500, document.RootElement.GetProperty("inSiteCloseCents").GetInt64());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("spreadPct").ValueKind);
    }

    [Fact]
    public async Task Reference_for_unknown_item_is_not_found()
    {
        var client = Factory.CreateClient();
        using var response = await client.GetAsync($"/api/market/items/{Guid.NewGuid()}/reference");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
