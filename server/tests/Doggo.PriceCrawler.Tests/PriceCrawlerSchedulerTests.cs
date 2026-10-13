using System.Net;
using Doggo.Domain.Price;
using Doggo.PriceCrawler;
using Doggo.PriceCrawler.Robots;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler.Tests;

public static class TestHttp
{
    public static HttpClient WithHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var client = new HttpClient(new StubHandler(handler));
        client.Timeout = TimeSpan.FromSeconds(5);
        return client;
    }

    public static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content)
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}

public sealed class PriceCrawlerSchedulerTests
{
    private static PriceCrawlerOptions Options(Action<PriceCrawlerOptions>? mutate = null)
    {
        var options = new PriceCrawlerOptions
        {
            Enabled = true,
            ScanIntervalSeconds = 10,
            Retry = new PriceCrawlerRetryOptions { MaxAttempts = 1, BaseDelaySeconds = 1, CapSeconds = 1 },
            CircuitBreaker = new PriceCrawlerCircuitOptions { FailureThreshold = 3, CooldownMinutes = 60 },
            Discover = new PriceCrawlerDiscoverOptions { IntervalHours = 24, RunAtUtc = "00:00" },
            Sites = [new PriceCrawlerSiteOptions { Key = "fake-site", IntervalMinutes = 60, MarketHashNames = [] }],
        };
        mutate?.Invoke(options);
        return options;
    }

    private static PriceCrawlerScheduler Scheduler(
        FakePriceCrawlStores stores,
        FakeSiteAdapter adapter,
        PriceCrawlerOptions options,
        HttpClient? robotsHttp = null)
    {
        var registry = new SiteAdapterRegistry([adapter]);
        var robots = new RobotsChecker(
            robotsHttp ?? new HttpClient(new EmptyHandler()),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<RobotsChecker>.Instance);
        return new PriceCrawlerScheduler(
            registry,
            stores.MapStore,
            stores.RawStore,
            stores.SeriesStore,
            stores.SiteStateStore,
            stores.ExchangeRates,
            robots,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PriceCrawlerScheduler>.Instance);
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
    }

    [Fact]
    public async Task DisabledCrawlerDoesNothing()
    {
        var stores = new FakePriceCrawlStores();
        var adapter = new FakeSiteAdapter { FetchResults = [new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow)] };
        var scheduler = Scheduler(stores, adapter, Options(o => o.Enabled = false));

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, adapter.FetchCalls);
        Assert.Empty(stores.Series);
    }

    [Fact]
    public async Task SiteRunsOnceThenRespectsInterval()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter { FetchResults = [new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow)] };
        var scheduler = Scheduler(stores, adapter, Options());

        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, adapter.FetchCalls);

        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, adapter.FetchCalls); // still inside the 60min interval

        var state = Assert.Single(stores.SiteStates);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.True(state.LastRunAtUtc is not null);
    }

    [Fact]
    public async Task SiteIsNotFetchedWhenItHasNoKnownMappings()
    {
        var stores = new FakePriceCrawlStores();
        var adapter = new FakeSiteAdapter();
        var scheduler = Scheduler(stores, adapter, Options());

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, adapter.FetchCalls);
        Assert.Equal(1, adapter.DiscoverCalls); // discovery still ran, finding nothing
        Assert.Single(stores.SiteStates);
    }

    [Fact]
    public async Task DiscoveryAddsMappings()
    {
        var stores = new FakePriceCrawlStores();
        var adapter = new FakeSiteAdapter
        {
            DiscoverResults = [new ExternalItemRef("ext-1", "AK-47", "https://fake.example/item/1")]
        };
        var scheduler = Scheduler(stores, adapter, Options());

        await scheduler.RunOnceAsync(CancellationToken.None);

        var map = Assert.Single(stores.Maps);
        Assert.Equal("ext-1", map.ExternalId);
        Assert.Equal(PriceMapStatus.Unconfirmed, map.MapStatus);
        Assert.Equal(PriceMapSource.Auto, map.MatchSource);
        Assert.True(stores.SiteStates[0].LastDiscoverAtUtc is not null);
    }

    [Fact]
    public async Task DiscoveryRunsOncePerInterval()
    {
        var stores = new FakePriceCrawlStores();
        var adapter = new FakeSiteAdapter
        {
            DiscoverResults = [new ExternalItemRef("ext-1", "t", "u")]
        };
        var scheduler = Scheduler(stores, adapter, Options());

        await scheduler.RunOnceAsync(CancellationToken.None);
        await scheduler.RunOnceAsync(CancellationToken.None);
        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, adapter.DiscoverCalls);
        Assert.Equal(1, stores.Maps.Count);
    }

    [Fact]
    public async Task FailedFetchCountsFailuresAndResetsOnSuccess()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter { FetchError = new HttpRequestException("down") };
        var scheduler = Scheduler(stores, adapter, Options(o => o.Sites[0].IntervalMinutes = 1));

        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, stores.SiteStates[0].ConsecutiveFailures);
        Assert.Equal("down", stores.SiteStates[0].LastError);

        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(2, stores.SiteStates[0].ConsecutiveFailures);

        adapter.FetchError = null;
        adapter.FetchResults.Add(new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow));
        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, stores.SiteStates[0].ConsecutiveFailures);
    }

    [Fact]
    public async Task CircuitOpensAfterThresholdFailuresAndSkipsWhileOpen()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter { FetchError = new HttpRequestException("down") };
        var options = Options(o =>
        {
            o.Sites[0].IntervalMinutes = 1;
            o.CircuitBreaker.FailureThreshold = 2;
        });
        var scheduler = Scheduler(stores, adapter, options);

        await scheduler.RunOnceAsync(CancellationToken.None);
        await scheduler.RunOnceAsync(CancellationToken.None);

        var state = Assert.Single(stores.SiteStates);
        Assert.True(state.CircuitOpenUntilUtc is not null && state.CircuitOpenUntilUtc > DateTime.UtcNow);

        var calls = adapter.FetchCalls;
        await scheduler.RunOnceAsync(CancellationToken.None);
        Assert.Equal(calls, adapter.FetchCalls); // circuit open: no new fetch attempts
    }

    [Fact]
    public async Task RobotsDisallowSkipsTheSite()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter
        {
            RobotsUrl = "https://fake.example/market/",
            FetchResults = [new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow)]
        };
        var options = Options(o => o.Sites[0].IntervalMinutes = 1);
        var http = TestHttp.WithHandler(_ => TestHttp.Ok("User-agent: *\nDisallow: /"));
        var scheduler = Scheduler(stores, adapter, options, http);

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, adapter.FetchCalls);
        Assert.Equal(0, adapter.DiscoverCalls); // robots gate blocks the whole site run
    }

    [Fact]
    public async Task SiteRobotsFlagBypassesRobotsCheck()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter
        {
            RobotsUrl = "https://fake.example/market/",
            FetchResults = [new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow)]
        };
        var options = Options(o =>
        {
            o.Sites[0].IntervalMinutes = 1;
            o.Sites[0].RobotsEnabled = false;
        });
        var http = TestHttp.WithHandler(_ => TestHttp.Ok("User-agent: *\nDisallow: /"));
        var scheduler = Scheduler(stores, adapter, options, http);

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, adapter.FetchCalls);
        Assert.Single(stores.Series);
    }

    [Fact]
    public async Task NightWindowPausesRuns()
    {
        var stores = new FakePriceCrawlStores();
        var map = await stores.MapStore.GetOrAddAsync("fake-site", "item-1", "t", "u", CancellationToken.None);
        map.MapStatus = PriceMapStatus.Confirmed;
        map.MarketItemId = Guid.CreateVersion7();
        var adapter = new FakeSiteAdapter
        {
            FetchResults = [new ExternalPriceFetch("item-1", 100, "CNY", DateTimeOffset.UtcNow)]
        };
        var options = Options(o =>
        {
            // Window built around "now": a fixed 00:00-23:59 window misses the 23:59:xx second.
            var now = DateTime.UtcNow;
            o.NightWindowUtc = new PriceCrawlerNightWindowOptions
            {
                Enabled = true,
                Start = now.AddMinutes(-1).ToString("HH:mm"),
                End = now.AddMinutes(1).ToString("HH:mm"),
            };
        });

        await Scheduler(stores, adapter, options).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, adapter.FetchCalls);
        Assert.Empty(stores.SiteStates); // no tick state is written
    }

    [Fact]
    public async Task MissingAdapterDoesNotBreakTheTick()
    {
        var stores = new FakePriceCrawlStores();
        var registry = new SiteAdapterRegistry([]);
        var options = Options();
        var scheduler = new PriceCrawlerScheduler(
            registry,
            stores.MapStore,
            stores.RawStore,
            stores.SeriesStore,
            stores.SiteStateStore,
            stores.ExchangeRates,
            new RobotsChecker(new HttpClient(new EmptyHandler()), Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<RobotsChecker>.Instance),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<PriceCrawlerScheduler>.Instance);

        await scheduler.RunOnceAsync(CancellationToken.None); // must not throw

        Assert.Empty(stores.Maps);
    }

    [Fact]
    public async Task PurgesExpiredRawRows()
    {
        var stores = new FakePriceCrawlStores();
        stores.Raws.Add(new PriceCrawlRaw
        {
            Id = Guid.CreateVersion7(),
            SiteKey = "fake-site",
            ExternalId = "item-1",
            FetchedAtUtc = DateTime.UtcNow.AddDays(-200),
            ObservedAtUtc = DateTime.UtcNow.AddDays(-200),
        });
        var adapter = new FakeSiteAdapter();
        var scheduler = Scheduler(stores, adapter, Options(o => o.RawRetentionDays = 90));

        await scheduler.RunOnceAsync(CancellationToken.None);

        Assert.Empty(stores.Raws);
    }
}
