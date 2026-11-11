using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class VaporWebhookApiTests : IClassFixture<VaporWebhookApiFactory>
{
    private const string Secret = "test-webhook-secret";
    private readonly VaporWebhookApiFactory _factory;

    public VaporWebhookApiTests(VaporWebhookApiFactory factory) => _factory = factory;

    private static HttpRequestMessage SignedRequest(Guid webhookId, string? secret = Secret, string? body = null)
    {
        var payload = body ?? JsonSerializer.Serialize(new
        {
            id = webhookId,
            category = "jobs",
            type = "job_finished",
            jobId = (Guid?)null,
            accountName = "acc1",
            state = "finished",
            message = "",
            timestamp = DateTimeOffset.UtcNow,
            payload = new { app_id = "730" }
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexString(VaporWebhook.ComputeSignature(secret ?? Secret, timestamp, payload))
            .ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vapor/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(VaporWebhook.TimestampHeader, timestamp);
        request.Headers.Add(VaporWebhook.SignatureHeader, $"sha256={signature}");
        return request;
    }

    private static async Task<JsonElement> SendAndReadJsonAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }

    [Fact]
    public async Task Signed_webhook_is_accepted_once()
    {
        var client = _factory.CreateClient();
        var webhookId = Guid.NewGuid();

        var body = await SendAndReadJsonAsync(client, SignedRequest(webhookId));
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.False(body.GetProperty("duplicate").GetBoolean());

        // A replay of the same event id is acknowledged but flagged as duplicate.
        var replayBody = await SendAndReadJsonAsync(client, SignedRequest(webhookId));
        Assert.True(replayBody.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Tampered_signature_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var request = SignedRequest(Guid.NewGuid(), secret: "wrong-secret");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_webhook_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vapor/webhook")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Well_signed_body_that_is_not_an_envelope_is_bad_request()
    {
        var client = _factory.CreateClient();
        var request = SignedRequest(Guid.NewGuid(), body: """{"not":"an envelope"}""");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- order retrieval completion is callback driven ----

    private static HttpRequestMessage TradeCompletedRequest(Guid webhookId, Guid orderId, string offerId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = webhookId,
            category = "order_retrieval",
            type = "trade_completed",
            jobId = (Guid?)null,
            accountName = (string?)null,
            state = (string?)null,
            message = (string?)null,
            timestamp = DateTimeOffset.UtcNow,
            payload = new { order_id = orderId, offer_id = offerId }
        });
        return SignedRequest(webhookId, body: payload);
    }

    private async Task<(HttpClient Client, Guid OrderId)> PaidOrderAsync()
    {
        var client = _factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            account = $"137{Random.Shared.Next(100000, 999999):D6}",
            password = "super-secret-1"
        });
        register.EnsureSuccessStatusCode();
        var registered = JsonDocument.Parse(await register.Content.ReadAsStringAsync()).RootElement.Clone();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());

        Guid productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            productId = Guid.CreateVersion7();
            db.Products.Add(new Product
            {
                Id = productId,
                Type = ProductType.Crate,
                Name = "Snakebite Case",
                ClassId = "493",
                PurchasePriceCents = 8000,
                ReferencePriceCents = 9000,
                AdjustPriceCents = 1000,
                FinalPriceCents = 10000,
                RegularPriceCents = 12000,
                Status = ProductStatus.OnShelf
            });
            await db.SaveChangesAsync();
        }

        var created = await client.PostAsJsonAsync("/api/orders", new
        {
            productId,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay
        });
        created.EnsureSuccessStatusCode();
        var orderId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("orderId").GetGuid();

        var callback = await client.PostAsJsonAsync("/api/payments/callback", new
        {
            orderId,
            amountCents = 10000,
            success = true
        });
        callback.EnsureSuccessStatusCode();
        return (client, orderId);
    }

    [Fact]
    public async Task Trade_completed_webhook_completes_the_order_when_the_offer_matches()
    {
        var (client, orderId) = await PaidOrderAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var order = await db.UserOrders.SingleAsync(o => o.Id == orderId);
            order.Status = OrderStatus.Retrieving;
            order.TradeOfferId = "offer-9";
            await db.SaveChangesAsync();
        }

        using (var response = await client.SendAsync(TradeCompletedRequest(Guid.NewGuid(), orderId, "offer-9")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var completed = await db2.UserOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Trade_completed_webhook_rejects_a_foreign_offer_and_keeps_the_order_retrieving()
    {
        var (client, orderId) = await PaidOrderAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var retrieving = await db.UserOrders.SingleAsync(o => o.Id == orderId);
            retrieving.Status = OrderStatus.Retrieving;
            retrieving.TradeOfferId = "offer-9";
            await db.SaveChangesAsync();
        }

        using (var response = await client.SendAsync(TradeCompletedRequest(Guid.NewGuid(), orderId, "offer-999")))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var order = await db2.UserOrders.SingleAsync(o => o.Id == orderId);
        Assert.Equal(OrderStatus.Retrieving, order.Status);
    }

    [Fact]
    public async Task Retrieval_complete_endpoint_is_no_longer_user_driven()
    {
        var (client, orderId) = await PaidOrderAsync();

        using var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/retrieval/complete", new { offerId = "offer-9" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
