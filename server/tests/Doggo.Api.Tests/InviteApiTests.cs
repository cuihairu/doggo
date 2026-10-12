using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class InviteApiTests(ApiFactory factory) : ApiTestBase(factory)
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

    private async Task<Guid> CurrentUserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return me.GetProperty("id").GetGuid();
    }

    /// <summary>Adds a rewarded invitee to the database and returns the log row id.</summary>
    private async Task<Guid> SeedInviteAsync(Guid inviterId, Guid inviteeId, string nickname, long reward, DateTime createdAtUtc)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var logId = Guid.CreateVersion7();
        db.UserAccounts.Add(new UserAccount
        {
            Id = inviteeId,
            Nickname = nickname,
            InviteCode = $"INV{nickname}",
            InvitedByUserId = inviterId
        });
        db.InviteRewardLogs.Add(new InviteRewardLog
        {
            Id = logId,
            InviterUserId = inviterId,
            InviteeUserId = inviteeId,
            CoinReward = reward,
            Note = "order:test",
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        });
        await db.SaveChangesAsync();
        return logId;
    }

    [Fact]
    public async Task Invite_rewards_require_a_valid_token()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/invites");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invite_rewards_sum_only_the_callers_own_rows_and_name_the_invitee()
    {
        var client = await AuthorizedClientAsync();
        var userId = await CurrentUserIdAsync(client);

        await SeedInviteAsync(userId, Guid.CreateVersion7(), "Alice", 100, DateTime.UtcNow.AddMinutes(-2));
        await SeedInviteAsync(userId, Guid.CreateVersion7(), "Bob", 50, DateTime.UtcNow.AddMinutes(-1));
        await SeedInviteAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), "Other", 999, DateTime.UtcNow);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/invites");

        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(150, body.GetProperty("totalReward").GetInt64());
        var items = body.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        // Newest first: Bob's reward is one minute newer than Alice's.
        Assert.Equal("Bob", items[0].GetProperty("inviteeNickname").GetString());
        Assert.Equal(50, items[0].GetProperty("coinReward").GetInt64());
        Assert.Equal("Alice", items[1].GetProperty("inviteeNickname").GetString());
        Assert.Equal(100, items[1].GetProperty("coinReward").GetInt64());
    }

    [Fact]
    public async Task Invite_rewards_page_the_result_set()
    {
        var client = await AuthorizedClientAsync();
        var userId = await CurrentUserIdAsync(client);

        await SeedInviteAsync(userId, Guid.CreateVersion7(), "First", 100, DateTime.UtcNow.AddMinutes(-3));
        await SeedInviteAsync(userId, Guid.CreateVersion7(), "Second", 100, DateTime.UtcNow.AddMinutes(-2));
        await SeedInviteAsync(userId, Guid.CreateVersion7(), "Third", 100, DateTime.UtcNow.AddMinutes(-1));

        var body = await client.GetFromJsonAsync<JsonElement>("/api/invites?page=2&pageSize=2");

        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(300, body.GetProperty("totalReward").GetInt64());
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("First", items[0].GetProperty("inviteeNickname").GetString());
    }
}
