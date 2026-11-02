using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class PrizeApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    /// <summary>Registers a user and attaches its bearer token; the prize endpoints require it.</summary>
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

    private async Task<Guid> SeedPrizeAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var prize = new UserPrize
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrderId = Guid.CreateVersion7(),
            Source = PrizeSource.GroupOpen,
            AssetId = "730-ak-redline-1",
            ClassId = "3250",
            InstanceId = "0",
            MarketHashName = "AK-47 | Redline (Field-Tested)",
            Quality = CaseQuality.Classified,
            Exterior = "Field-Tested",
            FloatValue = 0.2312,
            Rarity = "classified",
            IconUrl = "https://img.example/ak.png",
            PriceCents = 8800
        };
        db.UserPrizes.Add(prize);
        db.UserPrizeLogs.Add(new UserPrizeLog
        {
            Id = Guid.CreateVersion7(),
            PrizeId = prize.Id,
            UserId = userId,
            OrderId = prize.OrderId,
            Source = prize.Source
        });
        await db.SaveChangesAsync();
        return prize.Id;
    }

    [Fact]
    public async Task Prize_list_detail_and_logs_are_scoped_to_the_owner()
    {
        var (client, userId) = await AuthorizedAsync();
        var prizeId = await SeedPrizeAsync(userId);

        var list = await client.GetStringAsync($"/api/users/{userId}/prizes?page=1&pageSize=10");
        using (var listView = JsonDocument.Parse(list))
        {
            Assert.Equal(1, listView.RootElement.GetProperty("total").GetInt32());
            var item = listView.RootElement.GetProperty("items")[0];
            Assert.Equal("AK-47 | Redline (Field-Tested)", item.GetProperty("marketHashName").GetString());
            Assert.Equal(8800, item.GetProperty("priceCents").GetInt64());
            Assert.Null(item.GetProperty("marketPriceCents").GetString());
        }

        var detail = await client.GetStringAsync($"/api/users/{userId}/prizes/{prizeId}");
        using var detailView = JsonDocument.Parse(detail);
        Assert.Equal("730-ak-redline-1", detailView.RootElement.GetProperty("assetId").GetString());

        var logs = await client.GetStringAsync($"/api/users/{userId}/prizes/{prizeId}/logs");
        using var logsView = JsonDocument.Parse(logs);
        Assert.Single(logsView.RootElement.EnumerateArray());

        // Another user's prize id on the caller's own route is a real not-found; and a
        // cross-user route id never matches the caller, so it is also hidden as not-found.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{userId}/prizes/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{Guid.CreateVersion7()}/prizes/{prizeId}")).StatusCode);
    }
}
