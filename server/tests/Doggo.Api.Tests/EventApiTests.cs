using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>POST /api/events ingest contract (design §5): validation, dedup, rate limit.</summary>
public class EventApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private HttpClient NewClient()
    {
        var client = Factory.CreateClient();
        // A unique user agent yields a unique visitor key, isolating the rate limiter per test.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"event-tests-{Guid.NewGuid():N}");
        return client;
    }

    private static string EventJson(string eventName, string idempotencyKey, DateTime? occurredAtUtc = null, object? properties = null) =>
        JsonSerializer.Serialize(new
        {
            events = new[]
            {
                new
                {
                    eventName,
                    occurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
                    sessionId = Guid.CreateVersion7(),
                    channel = (string?)null,
                    schemaVersion = 1,
                    idempotencyKey,
                    path = "/products/abc?utm_source=x",
                    properties
                }
            }
        });

    private async Task<HttpResponseMessage> PostEventAsync(HttpClient client, string body)
    {
        var response = await client.PostAsync("/api/events", new StringContent(body, Encoding.UTF8, "application/json"));
        return response;
    }

    [Fact]
    public async Task Accepts_valid_batch_and_stamps_server_fields()
    {
        var client = NewClient();
        // Relative, never pinned to a calendar date: a fixed date ages out of
        // the 7 day ingest window and starts getting rejected as stale.
        var occurredAtUtc = DateTime.UtcNow.AddHours(-1);

        var response = await PostEventAsync(client, EventJson("page_view_product", $"acc-{Guid.NewGuid():N}", occurredAtUtc));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, result.GetProperty("accepted").GetInt32());
        Assert.Equal(0, result.GetProperty("duplicates").GetInt32());
        Assert.Equal(0, result.GetProperty("rejected").GetInt32());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "page_view_product");
        Assert.Equal(occurredAtUtc, evt.OccurredAtUtc);
        Assert.True(evt.ReceivedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal("/products/abc", evt.Path);
        Assert.Equal("organic", evt.Channel);
        Assert.Equal(32, evt.VisitorKey.Length);
    }

    [Fact]
    public async Task Rejects_unknown_event_names_and_stale_events()
    {
        var client = NewClient();

        var unknown = await PostEventAsync(client, EventJson("page_view_home", $"u-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        var unknownResult = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, unknownResult.GetProperty("rejected").GetInt32());
        Assert.Equal(0, unknownResult.GetProperty("accepted").GetInt32());

        var stale = await PostEventAsync(client, EventJson("checkout_intent", $"s-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(-8)));
        var staleResult = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, staleResult.GetProperty("rejected").GetInt32());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Empty(await db.AnalyticsEvents.ToListAsync());
    }

    [Fact]
    public async Task Oversized_batches_are_rejected_with_400()
    {
        var client = NewClient();
        var events = Enumerable.Range(0, 51).Select(i => new
        {
            eventName = "page_view_product",
            occurredAtUtc = DateTime.UtcNow,
            sessionId = Guid.CreateVersion7(),
            channel = (string?)null,
            schemaVersion = 1,
            idempotencyKey = $"big-{i}",
            path = (string?)null,
            properties = (object?)null
        });

        var response = await client.PostAsync("/api/events", new StringContent(
            JsonSerializer.Serialize(new { events }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Idempotency_keys_deduplicate_within_and_across_batches()
    {
        var client = NewClient();
        var key = $"dup-{Guid.NewGuid():N}";

        var first = JsonDocument.Parse(await (await PostEventAsync(client, EventJson("page_view_product", key))).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, first.GetProperty("accepted").GetInt32());

        var second = JsonDocument.Parse(await (await PostEventAsync(client, EventJson("page_view_product", key))).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, second.GetProperty("accepted").GetInt32());
        Assert.Equal(1, second.GetProperty("duplicates").GetInt32());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Equal(1, await db.AnalyticsEvents.CountAsync(e => e.IdempotencyKey == key));
    }

    [Fact]
    public async Task Properties_are_filtered_to_the_whitelist()
    {
        var client = NewClient();

        var response = await PostEventAsync(client, EventJson(
            "checkout_intent",
            $"wl-{Guid.NewGuid():N}",
            properties: new { productId = "p-1", quantity = 2, nickname = "evil", phone = "13900000000" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, result.GetProperty("accepted").GetInt32());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "checkout_intent");
        Assert.Contains("productId", evt.PropertiesJson);
        Assert.Contains("quantity", evt.PropertiesJson);
        Assert.DoesNotContain("nickname", evt.PropertiesJson);
        Assert.DoesNotContain("13900000000", evt.PropertiesJson);
    }

    [Fact]
    public async Task Authenticated_events_carry_the_user_id()
    {
        var account = "13900000040";
        var register = await Factory.CreateClient().PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();

        var client = NewClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());

        await PostEventAsync(client, EventJson("page_view_product", $"auth-{Guid.NewGuid():N}"));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.NotNull((await db.AnalyticsEvents.SingleAsync(e => e.EventName == "page_view_product")).UserId);
    }

    [Fact]
    public async Task Bursts_from_one_visitor_hit_the_rate_limit()
    {
        var client = NewClient();

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 31; i++)
        {
            var response = await PostEventAsync(client, EventJson("page_view_product", $"rl-{Guid.NewGuid():N}"));
            last = response.StatusCode;
            if (last == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}

/// <summary>GET /api/admin/funnel reads FunnelDailyStat (design §8.1).</summary>
public class AdminFunnelApiTests(ApiFactory factory) : ApiTestBase(factory)
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

    private async Task<HttpClient> PlainClientAsync(string account)
    {
        var client = Factory.CreateClient();
        var register = await client.PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();
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

    [Fact]
    public async Task Funnel_report_requires_the_admin_role()
    {
        var client = await PlainClientAsync("13900000041");
        var response = await client.GetAsync("/api/admin/funnel");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Funnel_report_returns_groups_rows_and_received_count()
    {
        var client = await AdminClientAsync("13900000042");
        var d1 = new DateOnly(2026, 10, 11);
        var d2 = new DateOnly(2026, 10, 12);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            db.FunnelDailyStats.AddRange(
                new FunnelDailyStat
                {
                    Id = Guid.CreateVersion7(),
                    Date = d1,
                    Client = AnalyticsClient.Web,
                    Channel = "organic",
                    ViewProductUv = 100,
                    CheckoutIntentUv = 30,
                    OrderCreatedUv = 10,
                    OrderPaidUv = 6
                },
                new FunnelDailyStat
                {
                    Id = Guid.CreateVersion7(),
                    Date = d2,
                    Client = AnalyticsClient.Web,
                    Channel = "organic",
                    ViewProductUv = 100,
                    CheckoutIntentUv = 50,
                    OrderCreatedUv = 20,
                    OrderPaidUv = 14
                });
            db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                Id = Guid.CreateVersion7(),
                EventName = "page_view_product",
                OccurredAtUtc = DateTime.UtcNow.AddHours(-1),
                ReceivedAtUtc = DateTime.UtcNow,
                SessionId = Guid.CreateVersion7(),
                VisitorKey = "vk",
                Client = AnalyticsClient.Web,
                Channel = "organic",
                IdempotencyKey = $"funnel-{Guid.NewGuid():N}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/admin/funnel?from=2026-10-11&to=2026-10-12&groupBy=client");
        Assert.True(response.IsSuccessStatusCode, $"funnel -> {(int)response.StatusCode}");
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("2026-10-11", root.GetProperty("from").GetString());
        Assert.Equal("2026-10-12", root.GetProperty("to").GetString());
        Assert.Equal("client", root.GetProperty("groupBy").GetString());

        var web = root.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("key").GetString() == "web");
        var steps = web.GetProperty("steps");
        Assert.Equal(4, steps.GetArrayLength());
        Assert.Equal(200, steps[0].GetProperty("uv").GetInt64());
        Assert.Equal(80, steps[1].GetProperty("uv").GetInt64());
        Assert.Equal(30, steps[2].GetProperty("uv").GetInt64());
        Assert.Equal(20, steps[3].GetProperty("uv").GetInt64());
        Assert.Equal(0.40m, steps[1].GetProperty("rate").GetDecimal());
        Assert.Equal(0.60m, steps[1].GetProperty("dropRate").GetDecimal());

        Assert.Equal(2, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(1, root.GetProperty("receivedToday").GetInt64());
    }
}
