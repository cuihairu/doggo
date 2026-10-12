using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class CoinApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync("/api/auth/register", Json(new
        {
            account = $"139{Random.Shared.Next(100000, 999999):D6}",
            password = "super-secret-1"
        }));
        response.EnsureSuccessStatusCode();
        var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task SeedLogAsync(Guid userId, long delta, CoinLogType type, DateTime createdAtUtc)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.UserCoinLogs.Add(new UserCoinLog
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = type,
            Delta = delta,
            BalanceAfter = delta,
            Note = "seed",
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CurrentUserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return me.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Coin_logs_require_a_valid_token()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/coins/logs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Coin_logs_return_only_the_callers_rows_paged()
    {
        var client = await AuthorizedClientAsync();
        var userId = await CurrentUserIdAsync(client);
        // Separate saves: the audit interceptor stamps CreatedAtUtc per batch, and the
        // endpoint orders by it, so the spend row must land newest.
        await SeedLogAsync(userId, 100, CoinLogType.Grant, DateTime.UtcNow.AddMinutes(-2));
        await SeedLogAsync(userId, -50, CoinLogType.Spend, DateTime.UtcNow.AddMinutes(-1));
        await SeedLogAsync(Guid.CreateVersion7(), 999, CoinLogType.Grant, DateTime.UtcNow);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/coins/logs?page=1&pageSize=1");

        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        // Newest first: the spend row is one minute newer than the grant row.
        Assert.Equal(-50, items[0].GetProperty("delta").GetInt64());
        Assert.Equal((int)CoinLogType.Spend, items[0].GetProperty("type").GetInt32());
        Assert.Equal(-50, items[0].GetProperty("balanceAfter").GetInt64());
    }
}
