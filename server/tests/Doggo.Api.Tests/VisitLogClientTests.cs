using Doggo.Domain.Analytics;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>
/// VisitLogMiddleware stamps the client from the X-Doggo-Client header (funnel design §9.3)
/// and skips health checks, analytics ingest and webhooks; the rows are written by the batch
/// worker off the request path, so the assertions wait for the flush.
/// </summary>
public class VisitLogClientTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData("miniapp", AnalyticsClient.Miniapp)]
    [InlineData("mobile", AnalyticsClient.Mobile)]
    [InlineData("admin", AnalyticsClient.Admin)]
    [InlineData("web", AnalyticsClient.Web)]
    [InlineData("unknown", AnalyticsClient.Web)]
    [InlineData(null, AnalyticsClient.Web)]
    public async Task Visit_log_stamps_client_from_header(string? header, AnalyticsClient expected)
    {
        var client = Factory.CreateClient();
        if (header is not null)
        {
            client.DefaultRequestHeaders.Add("X-Doggo-Client", header);
        }

        var response = await client.GetAsync("/api/news");
        response.EnsureSuccessStatusCode();

        var log = await WaitForLogAsync("/api/news");
        Assert.Equal(expected, log.Client);
    }

    [Fact]
    public async Task Visit_log_skips_health_events_and_webhooks()
    {
        var client = Factory.CreateClient();

        // The visit log exclusion holds whatever the endpoint does; some of these error (an
        // unknown order in the callback, an unsigned webhook), which the test host surfaces
        // as a pipeline exception — fine, the middleware's finally still decides the log.
        await SendTolerantAsync(client, HttpMethod.Get, "/healthz");
        await SendTolerantAsync(client, HttpMethod.Post, "/api/events", "{}");
        await SendTolerantAsync(client, HttpMethod.Post, "/api/vapor/webhook", "{}");
        await SendTolerantAsync(client, HttpMethod.Post, "/api/payments/callback",
            $"{{\"orderId\":\"{Guid.NewGuid()}\",\"amountCents\":100,\"success\":true}}");

        // Past the batch flush window: any wrongly queued row would have landed by now.
        await Task.Delay(2600);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var rows = await db.UserVisitLogs.AsNoTracking()
            .Where(v => v.Path == "/healthz"
                        || v.Path == "/api/events"
                        || v.Path == "/api/vapor/webhook"
                        || v.Path == "/api/payments/callback")
            .ToListAsync();
        Assert.Empty(rows);
    }

    private static async Task SendTolerantAsync(HttpClient client, HttpMethod method, string path, string? body = null)
    {
        try
        {
            var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }

            await client.SendAsync(request);
        }
        catch (Exception)
        {
            // Expected for endpoints that surface their error to the test client; the visit
            // log behavior under test is decided by the middleware, not the endpoint result.
        }
    }

    private async Task<UserVisitLog> WaitForLogAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var log = await db.UserVisitLogs.AsNoTracking()
                .Where(v => v.Path == path)
                .OrderByDescending(v => v.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (log is not null)
            {
                return log;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException($"No visit log row for {path} within 5s.");
    }
}
