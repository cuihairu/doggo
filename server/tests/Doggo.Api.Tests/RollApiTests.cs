using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class RollApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>Registers a user and attaches its bearer token; the roll mutations require it.</summary>
    private async Task<(HttpClient Client, Guid UserId)> AuthorizedAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            account = $"139{Random.Shared.Next(100000, 999999):D6}",
            password = "super-secret-1"
        });
        response.EnsureSuccessStatusCode();
        var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        return (client, me.GetProperty("id").GetGuid());
    }

    /// <summary>Registers a user, flags it admin, and logs back in so the token carries the role claim.</summary>
    private async Task<(HttpClient Client, Guid UserId)> AuthorizedAdminAsync()
    {
        var (client, userId) = await AuthorizedAsync();
        string phone;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Id == userId);
            account.IsAdmin = true;
            phone = account.Phone;
            await db.SaveChangesAsync();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            account = phone,
            password = "super-secret-1"
        });
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return (client, userId);
    }

    /// <summary>Creates a room via the user-facing POST; the template itself is admin-gated and seeded directly.</summary>
    private async Task<Guid> CreateRoomAsync(HttpClient client)
    {
        Guid templateId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var template = new RollRoomTemplate
            {
                Id = Guid.CreateVersion7(),
                Title = "Daily roll",
                RuleType = RollRuleType.LoginEntry,
                DurationMinutes = 60,
                EntryDeadlineMinutes = 5,
                MaxPrizeValueCents = 10000
            };
            db.RollRoomTemplates.Add(template);
            await db.SaveChangesAsync();
            templateId = template.Id;
        }

        var createRoom = await client.PostAsync("/api/roll/rooms", Json(new { templateId }));
        createRoom.EnsureSuccessStatusCode();
        using var roomDoc = JsonDocument.Parse(await createRoom.Content.ReadAsStringAsync());
        return roomDoc.RootElement.GetProperty("roomId").GetGuid();
    }

    private async Task SeedPrizeAsync(HttpClient admin, Guid roomId)
    {
        var response = await admin.PostAsync($"/api/roll/rooms/{roomId}/prizes", Json(new object[]
        {
            new { type = 1, coinAmount = 100, weight = 1, title = "Coins", imageUrl = "https://img.example/coins.png" },
            new { type = 0, coinAmount = 0, weight = 1, title = "Knife", imageUrl = "https://img.example/knife.png" }
        }));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Room_lifecycle_create_join_draw_claim_runs_through_the_api()
    {
        var (client, userId) = await AuthorizedAsync();
        var (admin, _) = await AuthorizedAdminAsync();
        var roomId = await CreateRoomAsync(client);
        await SeedPrizeAsync(admin, roomId);

        var running = await client.GetStringAsync("/api/roll/rooms");
        using (var runningDoc = JsonDocument.Parse(running))
        {
            Assert.Equal(1, runningDoc.RootElement.GetArrayLength());
            var view = runningDoc.RootElement[0];
            Assert.Equal(roomId, view.GetProperty("id").GetGuid());
            Assert.Equal(2, view.GetProperty("prizes").GetArrayLength());
        }

        var join = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { entryRef = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);

        var draw = await client.PostAsync($"/api/roll/rooms/{roomId}/draw", content: null);
        Assert.Equal(HttpStatusCode.OK, draw.StatusCode);
        using (var drawDoc = JsonDocument.Parse(await draw.Content.ReadAsStringAsync()))
        {
            Assert.Equal(3, drawDoc.RootElement.GetProperty("status").GetInt32()); // Drawn
            Assert.Equal(userId, drawDoc.RootElement.GetProperty("winnerUserId").GetGuid());
        }

        var claim = await client.PostAsync($"/api/roll/rooms/{roomId}/claim", Json(new { }));
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        using (var claimDoc = JsonDocument.Parse(await claim.Content.ReadAsStringAsync()))
        {
            Assert.Equal(roomId, claimDoc.RootElement.GetProperty("roomId").GetGuid());
        }

        var detail = await client.GetStringAsync($"/api/roll/rooms/{roomId}");
        using (var detailDoc = JsonDocument.Parse(detail))
        {
            Assert.Equal(5, detailDoc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(1, detailDoc.RootElement.GetProperty("participantCount").GetInt32());
        }

        // No running rooms remain after the draw.
        var listAfter = await client.GetStringAsync("/api/roll/rooms");
        using (var afterDoc = JsonDocument.Parse(listAfter))
        {
            Assert.Equal(0, afterDoc.RootElement.GetArrayLength());
        }
    }

    [Fact]
    public async Task Join_unknown_room_returns_404_and_duplicate_join_returns_400()
    {
        var (client, userId) = await AuthorizedAsync();

        var missing = await client.PostAsync($"/api/roll/rooms/{Guid.CreateVersion7()}/join", Json(new { entryRef = (string?)null }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var (admin, _) = await AuthorizedAdminAsync();
        var roomId = await CreateRoomAsync(client);
        await SeedPrizeAsync(admin, roomId);

        var first = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { entryRef = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var duplicate = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { entryRef = (string?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Claim_by_non_winner_returns_400()
    {
        // The winner (client A) joins and wins; a different user (client B) cannot claim.
        var (winner, winnerId) = await AuthorizedAsync();
        var (admin, _) = await AuthorizedAdminAsync();
        var roomId = await CreateRoomAsync(winner);
        await SeedPrizeAsync(admin, roomId);
        await winner.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { entryRef = (string?)null }));
        await winner.PostAsync($"/api/roll/rooms/{roomId}/draw", content: null);

        var (claimant, _) = await AuthorizedAsync();
        var response = await claimant.PostAsync($"/api/roll/rooms/{roomId}/claim", Json(new { }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_room_from_unknown_template_returns_400()
    {
        var (client, _) = await AuthorizedAsync();

        var response = await client.PostAsync("/api/roll/rooms", Json(new { templateId = Guid.CreateVersion7() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Template_and_prize_seeding_require_an_admin()
    {
        var (user, _) = await AuthorizedAsync();

        var template = await user.PostAsync("/api/roll/templates", Json(new
        {
            title = "Daily roll",
            ruleType = 1,
            durationMinutes = 60,
            entryDeadlineMinutes = 5,
            maxPrizeValueCents = 10000
        }));
        Assert.Equal(HttpStatusCode.Forbidden, template.StatusCode);

        var roomId = await CreateRoomAsync(user);
        var prizes = await user.PostAsync($"/api/roll/rooms/{roomId}/prizes", Json(new object[]
        {
            new { type = 1, coinAmount = 100, weight = 1, title = "Coins", imageUrl = "https://img.example/coins.png" }
        }));
        Assert.Equal(HttpStatusCode.Forbidden, prizes.StatusCode);

        var (admin, _) = await AuthorizedAdminAsync();
        var adminTemplate = await admin.PostAsync("/api/roll/templates", Json(new
        {
            title = "Admin roll",
            ruleType = 1,
            durationMinutes = 60,
            entryDeadlineMinutes = 5,
            maxPrizeValueCents = 10000
        }));
        Assert.Equal(HttpStatusCode.Created, adminTemplate.StatusCode);
        var adminPrizes = await admin.PostAsync($"/api/roll/rooms/{roomId}/prizes", Json(new object[]
        {
            new { type = 1, coinAmount = 100, weight = 1, title = "Coins", imageUrl = "https://img.example/coins.png" }
        }));
        Assert.Equal(HttpStatusCode.OK, adminPrizes.StatusCode);
    }
}
