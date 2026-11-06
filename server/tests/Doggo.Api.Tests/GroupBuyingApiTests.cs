using System.Net;
using System.Net.Http.Headers;
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
    /// <summary>Registers a user and attaches its bearer token; the group-buying mutations require it.</summary>
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

    private async Task<Guid> SeedPaidOrderAsync(Guid userId, Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrderNo = $"DO{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}",
            ProductId = productId,
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

    private async Task<Guid> SeedOrderWithStatusAsync(Guid userId, Guid productId, OrderStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OrderNo = $"DO{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}",
            ProductId = productId,
            TotalPriceCents = 10000,
            TotalCostCents = 10000,
            Status = status,
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
    public async Task Seat_view_matches_each_prize_to_its_own_seat_for_a_multi_seat_order()
    {
        var productId = await SeedCrateAsync();
        var (client, userId) = await AuthorizedAsync();
        var orderId = await SeedPaidOrderAsync(userId, productId);

        var (batchId, _) = await CreateBatchAsync(client, productId, seats: 2);
        await SeedSeatOpeningAsync(batchId, seatNo: 1, orderId);
        await SeedSeatOpeningAsync(batchId, seatNo: 2, orderId);

        var firstResult = await client.PostAsJsonAsync($"/api/group-buyings/{batchId}/seats/1/result", new
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
            historyId = 4242,
            rawJson = """{"drop":"raw1"}"""
        });
        firstResult.EnsureSuccessStatusCode();

        var secondResult = await client.PostAsJsonAsync($"/api/group-buyings/{batchId}/seats/2/result", new
        {
            assetId = "730-drop-2",
            classId = "493",
            instanceId = "0",
            marketHashName = "AWP | Asiimov",
            quality = (int)CaseQuality.Covert,
            exterior = "Field-Tested",
            floatValue = 0.2312,
            rarity = "covert",
            iconUrl = "https://img.example/awp.png",
            inspectLink = "https://steam.com/inspect/2",
            priceCents = 56000,
            historyId = 4243,
            rawJson = """{"drop":"raw2"}"""
        });
        secondResult.EnsureSuccessStatusCode();

        var detail = await client.GetStringAsync($"/api/group-buyings/{batchId}");
        using var view = JsonDocument.Parse(detail);
        var seats = view.RootElement.GetProperty("seats");

        var seat1 = seats.EnumerateArray().First(s => s.GetProperty("seatNo").GetInt32() == 1);
        var seat2 = seats.EnumerateArray().First(s => s.GetProperty("seatNo").GetInt32() == 2);
        Assert.Equal("730-drop-1", seat1.GetProperty("prize")!.GetProperty("assetId").GetString());
        Assert.Equal("730-drop-2", seat2.GetProperty("prize")!.GetProperty("assetId").GetString());
        Assert.Equal("AK-47 | Redline", seat1.GetProperty("prize")!.GetProperty("marketHashName").GetString());
        Assert.Equal("AWP | Asiimov", seat2.GetProperty("prize")!.GetProperty("marketHashName").GetString());
    }

    [Fact]
    public async Task Claim_sells_seats_until_the_batch_is_sold_out()
    {
        var productId = await SeedCrateAsync();
        var (client, userId) = await AuthorizedAsync();

        var (batchId, seatCount) = await CreateBatchAsync(client, productId, seats: 2);
        Assert.Equal(2, seatCount);

        var firstOrder = await SeedPaidOrderAsync(userId, productId);
        var first = await ClaimAsync(client, batchId, firstOrder);
        first.EnsureSuccessStatusCode();
        using (var firstView = JsonDocument.Parse(await first.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, firstView.RootElement.GetProperty("seatNo").GetInt32());
        }

        var secondOrder = await SeedPaidOrderAsync(userId, productId);
        var second = await ClaimAsync(client, batchId, secondOrder);
        second.EnsureSuccessStatusCode();

        var thirdOrder = await SeedPaidOrderAsync(userId, productId);
        var third = await ClaimAsync(client, batchId, thirdOrder);
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
        var (client, userId) = await AuthorizedAsync();
        var orderId = await SeedPaidOrderAsync(userId, productId);

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
        var (client, _) = await AuthorizedAsync();

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

    [Fact]
    public async Task Mutations_reject_other_users_orders_and_seats()
    {
        var productId = await SeedCrateAsync();
        var (owner, ownerId) = await AuthorizedAsync();
        var (other, otherId) = await AuthorizedAsync();

        var (batchId, _) = await CreateBatchAsync(owner, productId, seats: 2);
        var ownerOrderId = await SeedPaidOrderAsync(ownerId, productId);
        var otherOrderId = await SeedPaidOrderAsync(otherId, productId);

        // An attacker cannot claim a seat with someone else's order.
        var foreignClaim = await ClaimAsync(other, batchId, ownerOrderId);
        Assert.Equal(HttpStatusCode.NotFound, foreignClaim.StatusCode);

        // The owner claims with their own order, then the seat belongs to the owner.
        var ownClaim = await ClaimAsync(owner, batchId, ownerOrderId);
        ownClaim.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var seat = db.GroupBuyingSeats.First(s => s.GroupBuyingId == batchId && s.SeatNo == 1);
        seat.Status = SeatStatus.Locked;
        seat.OrderId = ownerOrderId;
        await db.SaveChangesAsync();

        // paid/release/open/advance on another user's seat are rejected.
        var foreignPaid = await other.PostAsync($"/api/group-buyings/{batchId}/seats/1/paid", content: null);
        Assert.Equal(HttpStatusCode.NotFound, foreignPaid.StatusCode);
        var foreignRelease = await other.PostAsync($"/api/group-buyings/{batchId}/seats/1/release", content: null);
        Assert.Equal(HttpStatusCode.NotFound, foreignRelease.StatusCode);
        var foreignAdvance = await other.PostAsJsonAsync($"/api/group-buyings/{batchId}/seats/1/advance", new { target = (int)SeatStatus.Opening });
        Assert.Equal(HttpStatusCode.NotFound, foreignAdvance.StatusCode);
        var foreignOpen = await other.PostAsync($"/api/group-buyings/{batchId}/seats/1/open", content: null);
        Assert.Equal(HttpStatusCode.NotFound, foreignOpen.StatusCode);
        var foreignResult = await other.PostAsJsonAsync($"/api/group-buyings/{batchId}/seats/1/result", new
        {
            assetId = "730-foreign-drop",
            classId = "493",
            instanceId = "0",
            marketHashName = "AK-47 | Redline",
            quality = (int)CaseQuality.Classified,
            exterior = "Field-Tested",
            floatValue = 0.2312,
            rarity = "classified",
            iconUrl = "https://img.example/ak.png",
            inspectLink = "https://steam.com/inspect/1",
            priceCents = 8800
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignResult.StatusCode);
    }

    [Fact]
    public async Task Claim_rejects_cancelled_or_mismatched_product_orders()
    {
        var productId = await SeedCrateAsync();
        var (client, userId) = await AuthorizedAsync();
        var (batchId, _) = await CreateBatchAsync(client, productId, seats: 2);

        var cancelledOrderId = await SeedOrderWithStatusAsync(userId, productId, OrderStatus.Cancelled);
        var cancelled = await ClaimAsync(client, batchId, cancelledOrderId);
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);

        var foreignProductId = await SeedCrateAsync();
        var mismatchedOrderId = await SeedOrderWithStatusAsync(userId, foreignProductId, OrderStatus.PendingSettlement);
        var mismatched = await ClaimAsync(client, batchId, mismatchedOrderId);
        Assert.Equal(HttpStatusCode.Conflict, mismatched.StatusCode);

        var detail = await client.GetStringAsync($"/api/group-buyings/{batchId}");
        using var view = JsonDocument.Parse(detail);
        Assert.Equal(0, view.RootElement.GetProperty("soldCount").GetInt32());
    }
}
