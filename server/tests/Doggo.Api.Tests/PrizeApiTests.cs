using System.Net;
using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class PrizeApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private async Task<Guid> SeedPrizeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var prize = new UserPrize
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
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
            UserId = UserId,
            OrderId = prize.OrderId,
            Source = prize.Source
        });
        await db.SaveChangesAsync();
        return prize.Id;
    }

    [Fact]
    public async Task Prize_list_detail_and_logs_are_scoped_to_the_owner()
    {
        var prizeId = await SeedPrizeAsync();
        var client = Factory.CreateClient();

        var list = await client.GetStringAsync($"/api/users/{UserId}/prizes?page=1&pageSize=10");
        using (var listView = JsonDocument.Parse(list))
        {
            Assert.Equal(1, listView.RootElement.GetProperty("total").GetInt32());
            var item = listView.RootElement.GetProperty("items")[0];
            Assert.Equal("AK-47 | Redline (Field-Tested)", item.GetProperty("marketHashName").GetString());
            Assert.Equal(8800, item.GetProperty("priceCents").GetInt64());
            Assert.Null(item.GetProperty("marketPriceCents").GetString());
        }

        var detail = await client.GetStringAsync($"/api/users/{UserId}/prizes/{prizeId}");
        using var detailView = JsonDocument.Parse(detail);
        Assert.Equal("730-ak-redline-1", detailView.RootElement.GetProperty("assetId").GetString());

        var logs = await client.GetStringAsync($"/api/users/{UserId}/prizes/{prizeId}/logs");
        using var logsView = JsonDocument.Parse(logs);
        Assert.Single(logsView.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{Guid.CreateVersion7()}/prizes/{prizeId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/users/{UserId}/prizes/{Guid.CreateVersion7()}")).StatusCode);
    }
}
