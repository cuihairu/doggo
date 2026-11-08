using System.Text.Json;
using Doggo.Domain.Cases;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class CaseOpeningWebhookHandlerTests
{
    /// <summary>Lets a test fail the next write the same way a stale optimistic lock would.</summary>
    private sealed class ThrowingDbContext(DbContextOptions<DoggoDbContext> options) : DoggoDbContext(options)
    {
        public bool ThrowOnNextSave { get; set; }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new DbUpdateConcurrencyException("simulated seat conflict");
            }

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    [Fact]
    public async Task Handler_propagates_seat_concurrency_conflicts()
    {
        var options = new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"webhook-concurrency-{Guid.NewGuid():N}")
            .Options;
        await using var db = new ThrowingDbContext(options);

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Snakebite Case",
            ClassId = "493"
        };
        var order = new UserOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            OrderNo = "DO-WEBHOOK-CONFLICT",
            ProductId = product.Id,
            Status = OrderStatus.PendingSettlement,
            ServiceMode = ServiceMode.CloudOpen,
            FulfillmentType = FulfillmentType.Spot
        };
        var batch = new GroupBuying
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Lot = "lot-webhook-conflict",
            VaporCaseId = "case-1",
            SeatCount = 1,
            Version = 1
        };
        var seat = new GroupBuyingSeat
        {
            Id = Guid.CreateVersion7(),
            GroupBuyingId = batch.Id,
            SeatNo = 1,
            Status = SeatStatus.Opening,
            OrderId = order.Id,
            Version = 1
        };
        db.AddRange(product, order, batch, seat);
        await db.SaveChangesAsync();

        db.ThrowOnNextSave = true;
        var vapor = new VaporClient(new HttpClient(), Options.Create(new VaporOptions()));
        var service = new GroupBuyingService(db, vapor, NullLogger<GroupBuyingService>.Instance);
        var handler = new CaseOpeningWebhookHandler(service, NullLogger<CaseOpeningWebhookHandler>.Instance);

        using var payload = JsonDocument.Parse($$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "category": "case_opening",
              "type": "opening_result",
              "timestamp": "2026-11-06T00:00:00Z",
              "payload": {
                "batch_id": "{{batch.Id}}",
                "seat_no": 1,
                "history_id": 42,
                "item": {
                  "asset_id": "730-webhook-drop",
                  "class_id": "493",
                  "instance_id": "0",
                  "market_hash_name": "AK-47 | Redline",
                  "quality": 3,
                  "exterior": "Field-Tested",
                  "float_value": 0.2312,
                  "rarity": "classified",
                  "icon_url": "https://img.example/ak.png",
                  "price_cents": 8800
                }
              }
            }
            """);
        var envelope = payload.RootElement.Deserialize<VaporWebhookEnvelope>(VaporJson.Options)
            ?? throw new InvalidOperationException("test envelope failed to deserialize");

        // A seat conflict is transient and may mean the event was not applied at all; swallowing
        // it would keep the idempotency claim and lose the event. Propagating it lets the
        // endpoint release the claim so vapor retries and the event is applied (or found applied).
        await Assert.ThrowsAsync<SeatConcurrencyException>(() => handler.HandleAsync(envelope));
    }
}
