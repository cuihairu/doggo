using Doggo.Domain.Price;
using Doggo.PriceCrawler;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler.Tests;

public sealed class FakeSiteAdapter : ISiteAdapter
{
    public string SiteKey { get; init; } = "fake-site";
    public string AdapterVersion { get; init; } = "1.0";
    public string RobotsUrl { get; init; } = "https://fake.example/";
    public IReadOnlyList<string> SupportedCurrencyCodes { get; init; } = ["CNY"];
    public SiteRateLimit SuggestedRateLimit { get; init; } = new(0, 1, null);

    public List<ExternalItemRef> DiscoverResults { get; set; } = [];
    public List<ExternalPriceFetch> FetchResults { get; set; } = [];
    public Exception? DiscoverError { get; set; }
    public Exception? FetchError { get; set; }
    public int DiscoverCalls { get; private set; }
    public int FetchCalls { get; private set; }

    public Task<IReadOnlyList<ExternalItemRef>> DiscoverAsync(DiscoveryQuery query, CancellationToken cancellationToken)
    {
        DiscoverCalls++;
        if (DiscoverError is not null)
        {
            throw DiscoverError;
        }

        return Task.FromResult(DiscoverResults as IReadOnlyList<ExternalItemRef>);
    }

    public Task<IReadOnlyList<ExternalPriceFetch>> FetchPricesAsync(IReadOnlyList<string> externalIds, CancellationToken cancellationToken)
    {
        FetchCalls++;
        if (FetchError is not null)
        {
            throw FetchError;
        }

        return Task.FromResult(FetchResults as IReadOnlyList<ExternalPriceFetch>);
    }
}

public sealed class PriceCrawlIngestorTests
{
    private static PriceCrawlerOptions Options() => new();

    private static PriceCrawlIngestor Ingestor(FakePriceCrawlStores stores) =>
        new(stores.MapStore, stores.RawStore, stores.SeriesStore, stores.ExchangeRates,
            Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<PriceCrawlIngestor>.Instance);

    private static IOptions<PriceCrawlerOptions> SiteOptions() => Microsoft.Extensions.Options.Options.Create(Options());

    [Fact]
    public async Task ConfirmedMapProducesSeriesPoint()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "AK-47", "https://fake.example/item/1", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 12500, "CNY", DateTimeOffset.UtcNow, "125.00")]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.RawStored);
        Assert.Equal(1, result.SeriesUpserted);
        var point = Assert.Single(stores.Series);
        Assert.Equal(map.Id, point.MapId);
        Assert.Equal(12500, point.PriceCents);
        Assert.Equal("CNY", point.CurrencyCode);
        Assert.Equal(1m, point.ExchangeRate);
        Assert.Equal(12500, point.ConvertedCents);
        Assert.Equal(12, point.WindowKey.Length);
        Assert.False(point.IsSuspect);
    }

    [Fact]
    public async Task UnconfirmedMapStoresRawButNoSeries()
    {
        var stores = new FakePriceCrawlStores();
        await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 12500, "CNY", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.RawStored);
        Assert.Equal(0, result.SeriesUpserted);
        Assert.Equal(1, result.SkippedUnconfirmed);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task RejectedMapIsSkippedEntirely()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Rejected;

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 12500, "CNY", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(0, result.RawStored);
        Assert.Empty(stores.Raws);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task InvalidPriceKeepsRawButNoSeries()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", -5, "CNY", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.RawStored);
        Assert.Equal(1, result.SkippedInvalid);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task UnknownCurrencySkipsSeries()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 100, "FAKE", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.SkippedInvalid);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task ForeignCurrencyConvertsWithExchangeRate()
    {
        var stores = new FakePriceCrawlStores();
        stores.Rates["USD"] = 7.25m;
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 1000, "USD", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.SeriesUpserted);
        var point = Assert.Single(stores.Series);
        Assert.Equal(7250, point.ConvertedCents);
        Assert.Equal(7.25m, point.ExchangeRate);
    }

    [Fact]
    public async Task MissingExchangeRateSkipsSeries()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 1000, "JPY", DateTimeOffset.UtcNow)]
        };

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(1, result.SkippedInvalid);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task SameWindowUpsertsInsteadOfDuplicating()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        // Fixed time: "now" can sit near an hour boundary and split the two points.
        var observed = new DateTimeOffset(2026, 10, 13, 10, 7, 0, TimeSpan.Zero);
        var adapter = new FakeSiteAdapter
        {
            FetchResults =
            [
                new ExternalPriceFetch("item-1", 10000, "CNY", observed),
                new ExternalPriceFetch("item-1", 11000, "CNY", observed.AddMinutes(5)),
            ]
        };

        await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        var point = Assert.Single(stores.Series);
        Assert.Equal(11000, point.PriceCents);
        Assert.Equal(2, stores.Raws.Count);
    }

    [Fact]
    public async Task OutlierIsFlaggedSuspectButStillStored()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();

        var start = DateTimeOffset.UtcNow.AddDays(-10);
        var adapter = new FakeSiteAdapter();
        adapter.FetchResults.AddRange(Enumerable.Range(1, 10)
            .Select(i => new ExternalPriceFetch("item-1", 10000, "CNY", start.AddHours(i))));
        adapter.FetchResults.Add(new ExternalPriceFetch("item-1", 90000, "CNY", start.AddHours(11)));
        adapter.FetchResults.Add(new ExternalPriceFetch("item-1", 10100, "CNY", start.AddHours(12)));

        var result = await Ingestor(stores).IngestAsync(adapter, ["item-1"], CancellationToken.None);

        Assert.Equal(12, result.SeriesUpserted);
        Assert.Equal(1, result.Suspect);
        Assert.Contains(stores.Series, p => p.IsSuspect && p.PriceCents == 90000);
        Assert.Equal(1, stores.Series.Count(p => p.IsSuspect));
    }
}
