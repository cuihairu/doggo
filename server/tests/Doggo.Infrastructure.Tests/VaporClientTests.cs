using System.Net;
using System.Text;
using Doggo.Infrastructure.Vapor;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class VaporClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await Responder(request);
        }
    }

    private static VaporClient CreateClient(StubHandler handler, VaporOptions? options = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://vapor.test/") };
        return new VaporClient(httpClient, Options.Create(options ?? new VaporOptions
        {
            BaseUrl = "http://vapor.test",
            ApiKey = "key",
            JobPollIntervalMilliseconds = 10,
            JobTimeoutSeconds = 5
        }));
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetMarketListings_dispatches_polls_and_parses_output()
    {
        var jobId = Guid.NewGuid();
        var handler = new StubHandler
        {
            Responder = async request =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Response(HttpStatusCode.Accepted, $$"""{"id":"{{jobId}}"}""");
                }

                var output = """
                    {"app_id":"730","total_count":1,"start":0,"page_size":1,"has_more":false,
                     "listings":[{"listingId":"019a0000-0000-7000-8000-0000000000aa","name":"Snakebite Case",
                       "hashName":"Snakebite Case","sellListings":1234,"appId":"730","assetId":"",
                       "classId":"493","instanceId":"0","totalPrice":10.5,"currencyId":23,
                       "fetchedAt":"2026-10-11T11:00:00+00:00"}]}
                    """;
                var job = $$"""
                    {"id":"{{jobId}}","action":"get_market_listings","state":"completed",
                     "createdAt":"2026-10-11T12:00:00+00:00","tasks":[{"id":"{{Guid.NewGuid()}}",
                     "action":"get_market_listings","status":"finished","target":"acc1",
                     "output":{{output}},"error":null,"attempt":1,
                     "startedAt":"2026-10-11T12:00:00+00:00","finishedAt":"2026-10-11T12:00:01+00:00"}]}
                    """;
                return Response(HttpStatusCode.OK, job);
            }
        };

        var client = CreateClient(handler);
        var view = await client.GetMarketListingsAsync("730", 0, 100);

        Assert.Equal("730", view.AppId);
        Assert.Equal(1, view.TotalCount);
        Assert.False(view.HasMore);
        var listing = Assert.Single(view.Listings);
        Assert.Equal("Snakebite Case", listing.HashName);
        Assert.Equal("493", listing.ClassId);
        Assert.Equal(10.5m, listing.TotalPrice);
        Assert.Equal(23, listing.CurrencyId);

        // The dispatch must carry the action payload with snake_case keys and the bearer key.
        var dispatch = handler.Requests.First(r => r.Method == HttpMethod.Post);
        Assert.Equal("Bearer key", dispatch.Headers.Authorization?.ToString());
        var dispatchBody = await dispatch.Content!.ReadAsStringAsync();
        Assert.Contains("\"action\":\"get_market_listings\"", dispatchBody);
        Assert.Contains("\"app_id\":\"730\"", dispatchBody);
        Assert.Contains("\"count\":100", dispatchBody);
    }

    [Fact]
    public async Task GetMarketListings_throws_when_task_failed()
    {
        var jobId = Guid.NewGuid();
        var handler = new StubHandler
        {
            Responder = request => Task.FromResult(request.Method == HttpMethod.Post
                ? Response(HttpStatusCode.Accepted, $$"""{"id":"{{jobId}}"}""")
                : Response(HttpStatusCode.OK, $$"""
                      {"id":"{{jobId}}","action":"get_market_listings","state":"failed",
                       "createdAt":"2026-10-11T12:00:00+00:00","tasks":[{"id":"{{Guid.NewGuid()}}",
                       "action":"get_market_listings","status":"failed","target":"acc1","output":null,
                       "error":"steam request throttled","attempt":3,"startedAt":null,"finishedAt":null}]}
                      """))
        };

        var client = CreateClient(handler);
        var error = await Assert.ThrowsAsync<VaporClientException>(() => client.GetMarketListingsAsync("730", 0, 100));
        Assert.Contains("steam request throttled", error.Message);
    }

    [Fact]
    public async Task GetMarketListings_times_out_when_job_never_finishes()
    {
        var jobId = Guid.NewGuid();
        var handler = new StubHandler
        {
            Responder = request => Task.FromResult(request.Method == HttpMethod.Post
                ? Response(HttpStatusCode.Accepted, $$"""{"id":"{{jobId}}"}""")
                : Response(HttpStatusCode.OK, $$"""
                      {"id":"{{jobId}}","action":"get_market_listings","state":"running",
                       "createdAt":"2026-10-11T12:00:00+00:00","tasks":[{"id":"{{Guid.NewGuid()}}",
                       "action":"get_market_listings","status":"running","target":"acc1","output":null,
                       "error":null,"attempt":1,"startedAt":null,"finishedAt":null}]}
                      """))
        };

        var client = CreateClient(handler, new VaporOptions
        {
            BaseUrl = "http://vapor.test",
            ApiKey = "key",
            JobPollIntervalMilliseconds = 10,
            JobTimeoutSeconds = 1
        });

        await Assert.ThrowsAsync<VaporClientException>(() => client.GetMarketListingsAsync("730", 0, 100));
    }

    [Fact]
    public async Task Calls_fail_when_not_configured()
    {
        var handler = new StubHandler
        {
            Responder = _ => throw new InvalidOperationException("no request should be sent")
        };

        var client = CreateClient(handler, new VaporOptions());

        await Assert.ThrowsAsync<VaporClientException>(() => client.GetJobAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<VaporClientException>(() => client.DispatchJobAsync("get_market_listings"));
    }

    [Fact]
    public async Task Http_error_is_surfaced_in_exception()
    {
        var handler = new StubHandler
        {
            Responder = _ => Task.FromResult(Response(HttpStatusCode.Unauthorized, """{"error":"bad key"}"""))
        };

        var client = CreateClient(handler);
        var error = await Assert.ThrowsAsync<VaporClientException>(() => client.DispatchJobAsync("get_market_listings"));
        Assert.Contains("401", error.Message);
    }
}
