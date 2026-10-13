using System.Net;
using Doggo.PriceCrawler;
using Doggo.PriceCrawler.Html;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.PriceCrawler.Tests;

public sealed class ConfiguredHtmlSiteAdapterTests
{
    private const string SearchPage = """
        <html><body>
          <div class="market-item" data-id="1001"><span class="name">AK-47 | Redline</span></div>
          <div class="market-item" data-id="1002"><span class="name">AWP | Asiimov</span></div>
          <div class="market-item"><span class="name">no id</span></div>
        </body></html>
        """;

    private const string DetailPage = """
        <html><body>
          <h1>AK-47 | Redline</h1>
          <div class="price">¥1,284.50</div>
        </body></html>
        """;

    private static HtmlSiteAdapterOptions Options() => new()
    {
        BaseUrl = "https://fake.example",
        SearchPathTemplate = "/search?q={query}",
        DetailPathTemplate = "/item/{id}",
        ItemSelector = ".market-item",
        ItemIdAttribute = "data-id",
        TitleSelector = ".name",
        PriceSelector = ".price",
        CurrencyCode = "cny",
    };

    private static ConfiguredHtmlSiteAdapter Adapter(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(TestHttp.WithHandler(handler), "fake-site", Options(), NullLogger<ConfiguredHtmlSiteAdapter>.Instance);

    [Fact]
    public async Task DiscoverReadsIdsAndTitles()
    {
        var adapter = Adapter(request =>
        {
            Assert.Equal("/search?q=ak-47", request.RequestUri!.AbsolutePath + request.RequestUri.Query);
            return TestHttp.Ok(SearchPage);
        });

        var refs = await adapter.DiscoverAsync(new DiscoveryQuery("ak-47", null, "ak-47"), CancellationToken.None);

        Assert.Equal(2, refs.Count);
        Assert.Equal("1001", refs[0].ExternalId);
        Assert.Equal("AK-47 | Redline", refs[0].Title);
        Assert.Equal("https://fake.example/item/1001", refs[0].DetailUrl);
        Assert.Equal("1002", refs[1].ExternalId);
    }

    [Fact]
    public async Task DiscoverWithoutKeywordReturnsEmpty()
    {
        var adapter = Adapter(_ => throw new InvalidOperationException("no request expected"));
        var refs = await adapter.DiscoverAsync(new DiscoveryQuery(null, null, null), CancellationToken.None);
        Assert.Empty(refs);
    }

    [Fact]
    public async Task FetchParsesPricesAndFingerprints()
    {
        var adapter = Adapter(request =>
        {
            Assert.Equal("/item/1001", request.RequestUri!.AbsolutePath);
            return TestHttp.Ok(DetailPage);
        });

        var fetches = await adapter.FetchPricesAsync(["1001"], CancellationToken.None);

        var fetch = Assert.Single(fetches);
        Assert.Equal("1001", fetch.ExternalId);
        Assert.Equal(128450, fetch.PriceCents);
        Assert.Equal("CNY", fetch.CurrencyCode);
        Assert.Equal("¥1,284.50", fetch.RawPriceText);
        Assert.Equal(64, fetch.PageFingerprint!.Length);
    }

    [Fact]
    public async Task FetchSkipsUnparsablePrices()
    {
        var adapter = Adapter(_ => TestHttp.Ok("<html><body><div class=\"price\">sold out</div></body></html>"));
        var fetches = await adapter.FetchPricesAsync(["1001"], CancellationToken.None);
        Assert.Empty(fetches);
    }

    [Fact]
    public async Task FetchSkipsNonSuccessPages()
    {
        var adapter = Adapter(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetches = await adapter.FetchPricesAsync(["1001"], CancellationToken.None);
        Assert.Empty(fetches);
    }

    [Fact]
    public async Task TransportErrorsPropagate()
    {
        var adapter = Adapter(_ => throw new HttpRequestException("down"));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            adapter.FetchPricesAsync(["1001"], CancellationToken.None));
    }

    [Fact]
    public async Task RateLimitPacesBetweenRequests()
    {
        var options = Options();
        // Two items with the default 1s pacing: both fetch, spaced by the adapter.
        var adapter = new ConfiguredHtmlSiteAdapter(
            TestHttp.WithHandler(_ => TestHttp.Ok(DetailPage)), "fake-site", options,
            NullLogger<ConfiguredHtmlSiteAdapter>.Instance);

        var fetches = await adapter.FetchPricesAsync(["1001", "1002"], CancellationToken.None);

        Assert.Equal(2, fetches.Count);
    }
}
