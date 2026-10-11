using Doggo.Scraper.News;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Scraper.Tests;

public sealed class FlakyHandler : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _calls++;
        if (_calls <= FailTimes)
        {
            await Task.Delay(30, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("recovered"),
        };
    }

    public int FailTimes { get; init; }
}

public class HttpPageFetcherTests
{
    private static HttpPageFetcher Build(FlakyHandler handler) =>
        new(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) },
            NullLogger<HttpPageFetcher>.Instance,
            Options.Create(new ScraperOptions { RetryDelaySeconds = 0 }));

    [Fact]
    public async Task Transient_failures_are_retried_until_success()
    {
        var handler = new FlakyHandler { FailTimes = 2 };
        var fetcher = Build(handler);

        var body = await fetcher.FetchAsync("https://example.com/feed", CancellationToken.None);

        Assert.Equal("recovered", body);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Exhausted_retries_throw()
    {
        var handler = new FlakyHandler { FailTimes = 99 };
        var fetcher = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => fetcher.FetchAsync("https://example.com/feed", CancellationToken.None));

        Assert.Equal(3, handler.Calls);
    }
}
