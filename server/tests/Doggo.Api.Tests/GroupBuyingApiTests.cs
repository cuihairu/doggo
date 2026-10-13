using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    private static async Task<HttpResponseMessage> ClaimAsync(HttpClient client, Guid batchId) =>
        await client.PostAsJsonAsync($"/api/group-buyings/{batchId}/claim", new { orderId = Guid.CreateVersion7() });

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
