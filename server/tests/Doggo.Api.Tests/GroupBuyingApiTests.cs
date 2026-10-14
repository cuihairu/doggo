using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class GroupBuyingApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<Guid> SeedCrateAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Snakebite Case",
            ClassId = "493",
            PurchasePriceCents = 8000,
            ReferencePriceCents = 9000,
            AdjustPriceCents = 1000,
            FinalPriceCents = 10000,
            RegularPriceCents = 12000
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<(Guid BatchId, int SeatCount)> CreateBatchAsync(HttpClient client, Guid productId, int seats)
    {
        var response = await client.PostAsJsonAsync("/api/group-buyings", new
        {
            productId,
            lot = $"lot-{Guid.NewGuid():N}",
            vaporCaseId = "case-1",
            seats = Enumerable.Range(1, seats).Select(i => new { crateAssetId = $"crate-{i}", keyAssetId = $"key-{i}" })
        });
        response.EnsureSuccessStatusCode();
        using var view = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (view.RootElement.GetProperty("batchId").GetGuid(), view.RootElement.GetProperty("seatCount").GetInt32());
    }

    private async Task<Guid> SeedPaidOrderAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            OrderNo = $"DO{DateTime.UtcNow:yyyyMMddHHmmss}0001",
            TotalPriceCents = 10000,
            TotalCostCents = 10000,
            Status = OrderStatus.PendingSettlement,
            ServiceMode = ServiceMode.CloudOpen,
            FulfillmentType = FulfillmentType.Spot
        };
        db.UserOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid batchId) =>
        await ClaimAsync(client, batchId, Guid.CreateVersion7());

    private static async Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid batchId, Guid orderId) =>
        await client.PostAsJsonAsync($"/api/group-buyings/{batchId}/claim", new { orderId });

    // Claim/paid/open need a live vapor instance (external dependency); the test seeds the
    // seat directly into Opening so only the result call + read-back view are exercised.
    private async Task SeedSeatOpeningAsync(Guid batchId, int seatNo, Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batchId && s.SeatNo == seatNo);
        seat.Status = SeatStatus.Opening;
        seat.OrderId = orderId;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Claim_sells_seats_until_the_batch_is_sold_out()
    {
        var productId = await SeedCrateAsync();
        var client = Factory.CreateClient();

        var (batchId, seatCount) = await CreateBatchAsync(client, productId, seats: 2);
        Assert.Equal(2, seatCount);

        var first = await ClaimAsync(client, batchId);
        first.EnsureSuccessStatusCode();
        using (var firstView = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, firstView.RootElement.GetProperty("seatNo").GetInt32());
        }

        var second = await ClaimAsync(client, batchId);
        second.EnsureSuccessStatusCode();

        var third = await ClaimAsync(client, batchId);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);

        var detail = await client.GetStringAsync($"/api/group-buyings/{batchId}");
        using var detailView = JsonDocument.Parse(detail);
        Assert.Equal(2, detailView.RootElement.GetProperty("soldCount").GetInt32());
        Assert.Equal(1, (int)detailView.RootElement.GetProperty("status").GetInt32()); // SoldOut
    }

    [Fact]
    public async Task Seat_view_exposes_prize_details_once_opened()
    {
        var productId = await SeedCrateAsync();
        var orderId = await SeedPaidOrderAsync();
        var client = Factory.CreateClient();

        var (batchId, _) = await CreateBatchAsync(client, productId, seats: 2);
        await SeedSeatOpeningAsync(batchId, seatNo: 1, orderId);

        var result = await client.PostAsJsonAsync($"/api/group-buyings/{batchId}/seats/1/result", new
        {
            assetId = "730-drop-1",
            classId = "493",
            instanceId = "0",
            marketHashName = "AK-47 | Redline",
            quality = (int)CaseQuality.Classified,
            exterior = "Field-Tested",
            floatValue = 0.2312,
            rarity = "classified",
            iconUrl = "https://img.example/ak.png",
            inspectLink = "https://steam.com/inspect/1",
            priceCents = 8800,
            tradableAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            historyId = 4242,
            rawJson = """{"drop":"raw"}"""
        });
        result.EnsureSuccessStatusCode();

        var detail = await client.GetStringAsync($"/api/group-buyings/{batchId}");
        using var view = JsonDocument.Parse(detail);
        var seats = view.RootElement.GetProperty("seats");

        var seat1 = seats.EnumerateArray().First(s => s.GetProperty("seatNo").GetInt32() == 1);
        Assert.Equal((int)SeatStatus.Opened, seat1.GetProperty("status").GetInt32());
        Assert.Equal("730-drop-1", seat1.GetProperty("prizeAssetId").GetString());

        var prize = seat1.GetProperty("prize");
        Assert.Equal("AK-47 | Redline", prize.GetProperty("marketHashName").GetString());
        Assert.Equal((int)CaseQuality.Classified, prize.GetProperty("quality").GetInt32());
        Assert.Equal("Field-Tested", prize.GetProperty("exterior").GetString());
        Assert.Equal(0.2312, prize.GetProperty("floatValue").GetDouble());
        Assert.Equal("classified", prize.GetProperty("rarity").GetString());
        Assert.Equal("https://img.example/ak.png", prize.GetProperty("iconUrl").GetString());
        Assert.Equal("https://steam.com/inspect/1", prize.GetProperty("inspectLink").GetString());
        Assert.Equal(8800, prize.GetProperty("priceCents").GetInt64());
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), prize.GetProperty("tradableAtUtc").GetDateTime());

        var seat2 = seats.EnumerateArray().First(s => s.GetProperty("seatNo").GetInt32() == 2);
        Assert.Equal((int)SeatStatus.OnSale, seat2.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, seat2.GetProperty("prize").ValueKind);
    }

    [Fact]
    public async Task Create_rejects_unknown_products_and_unknown_batches_404()
    {
        var client = Factory.CreateClient();

        var bad = await client.PostAsJsonAsync("/api/group-buyings", new
        {
            productId = Guid.CreateVersion7(),
            lot = $"lot-{Guid.NewGuid():N}",
            vaporCaseId = "case-1",
            seats = new[] { new { crateAssetId = "crate-1" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var missing = await ClaimAsync(client, Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
