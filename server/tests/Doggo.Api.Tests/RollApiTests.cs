using System.Net;
using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class RollApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<Guid> CreateRoomAsync(HttpClient client)
    {
        var createTemplate = await client.PostAsync("/api/roll/templates", Json(new
        {
            title = "Daily roll",
            ruleType = 1, // LoginEntry
            durationMinutes = 60,
            entryDeadlineMinutes = 5,
            maxPrizeValueCents = 10000
        }));
        createTemplate.EnsureSuccessStatusCode();
        using var templateDoc = JsonDocument.Parse(await createTemplate.Content.ReadAsStringAsync());
        var templateId = templateDoc.RootElement.GetProperty("id").GetGuid();

        var createRoom = await client.PostAsync("/api/roll/rooms", Json(new { templateId }));
        createRoom.EnsureSuccessStatusCode();
        using var roomDoc = JsonDocument.Parse(await createRoom.Content.ReadAsStringAsync());
        return roomDoc.RootElement.GetProperty("roomId").GetGuid();
    }

    private async Task SeedPrizeAsync(Guid roomId)
    {
        var response = await Factory.CreateClient().PostAsync($"/api/roll/rooms/{roomId}/prizes", Json(new object[]
        {
            new { type = 1, coinAmount = 100, weight = 1, title = "Coins", imageUrl = "https://img.example/coins.png" },
            new { type = 0, coinAmount = 0, weight = 1, title = "Knife", imageUrl = "https://img.example/knife.png" }
        }));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Room_lifecycle_create_join_draw_claim_runs_through_the_api()
    {
        var client = Factory.CreateClient();
        var roomId = await CreateRoomAsync(client);
        await SeedPrizeAsync(roomId);

        var running = await client.GetStringAsync("/api/roll/rooms");
        using (var runningDoc = JsonDocument.Parse(running))
        {
            Assert.Equal(1, runningDoc.RootElement.GetArrayLength());
            var view = runningDoc.RootElement[0];
            Assert.Equal(roomId, view.GetProperty("id").GetGuid());
            Assert.Equal(2, view.GetProperty("prizes").GetArrayLength());
        }

        var join = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { userId = UserId, entryRef = (string?)null }));
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);

        var draw = await client.PostAsync($"/api/roll/rooms/{roomId}/draw", content: null);
        Assert.Equal(HttpStatusCode.OK, draw.StatusCode);
        using (var drawDoc = JsonDocument.Parse(await draw.Content.ReadAsStringAsync()))
        {
            Assert.Equal(3, drawDoc.RootElement.GetProperty("status").GetInt32()); // Drawn
            Assert.Equal(UserId, drawDoc.RootElement.GetProperty("winnerUserId").GetGuid());
        }

        var claim = await client.PostAsync($"/api/roll/rooms/{roomId}/claim", Json(new { userId = UserId }));
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
        var client = Factory.CreateClient();

        var missing = await client.PostAsync($"/api/roll/rooms/{Guid.CreateVersion7()}/join", Json(new { userId = UserId }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var roomId = await CreateRoomAsync(client);
        await SeedPrizeAsync(roomId);

        var first = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { userId = UserId }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var duplicate = await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { userId = UserId }));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Claim_by_non_winner_returns_400()
    {
        var client = Factory.CreateClient();
        var roomId = await CreateRoomAsync(client);
        await SeedPrizeAsync(roomId);
        await client.PostAsync($"/api/roll/rooms/{roomId}/join", Json(new { userId = UserId }));
        await client.PostAsync($"/api/roll/rooms/{roomId}/draw", content: null);

        var response = await client.PostAsync($"/api/roll/rooms/{roomId}/claim", Json(new { userId = Guid.CreateVersion7() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_room_from_unknown_template_returns_400()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/api/roll/rooms", Json(new { templateId = Guid.CreateVersion7() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
