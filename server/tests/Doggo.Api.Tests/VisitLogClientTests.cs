using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>VisitLogMiddleware stamps the client from the X-Doggo-Client header (funnel design §9.3).</summary>
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

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var log = await db.UserVisitLogs
            .Where(v => v.Path == "/api/news")
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstAsync();

        Assert.Equal(expected, log.Client);
    }
}
