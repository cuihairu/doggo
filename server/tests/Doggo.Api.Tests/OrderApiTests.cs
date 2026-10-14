using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class OrderApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private async Task<Product> SeedCrateAsync(
        long finalPriceCents = 10000,
        ProductStatus status = ProductStatus.OnShelf,
        string iconUrl = "")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Snakebite Case",
            ClassId = "493",
            IconUrl = iconUrl,
            PurchasePriceCents = 8000,
            ReferencePriceCents = 9000,
            AdjustPriceCents = 1000,
            FinalPriceCents = finalPriceCents,
            RegularPriceCents = 12000,
            Status = status
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private async Task<Guid> SeedPlanAsync(Guid productId, string title)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var plan = new CasePlan { Id = Guid.CreateVersion7(), ProductId = productId, Title = title };
        db.CasePlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task<Guid> SeedPromotionAsync(Guid productId, int stock, long discountPriceCents)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var promotion = new PromotionPlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = productId,
            DiscountPriceCents = discountPriceCents,
            Stock = stock,
            Enabled = true
        };
        db.PromotionPlans.Add(promotion);
        await db.SaveChangesAsync();
        return promotion.Id;
    }

    private static async Task<JsonElement> CreateOrderAsync(HttpClient client, Guid productId, Guid? planId = null)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            userId = UserId,
            productId,
            planId,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay
        });
        response.EnsureSuccessStatusCode();
        // Cloned: the JsonDocument is not kept alive by the test.
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> GetOrderAsync(HttpClient client, Guid orderId)
    {
        var response = await client.GetAsync($"/api/orders/{orderId}");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> PayCallbackAsync(HttpClient client, Guid orderId, long amountCents, bool success = true) =>
        await client.PostAsJsonAsync("/api/payments/callback", new
        {
            orderId,
            amountCents,
            success,
            message = success ? null : "insufficient balance"
        });

    [Fact]
    public async Task Full_flow_create_pay_callback_refund()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var client = Factory.CreateClient();

        var created = await CreateOrderAsync(client, product.Id);
        var orderId = created.GetProperty("orderId").GetGuid();
        Assert.Equal(10000, created.GetProperty("totalPriceCents").GetInt64());
        Assert.Equal(10000, created.GetProperty("totalCostCents").GetInt64());
        Assert.Equal("DO", created.GetProperty("orderNo").GetString()?[..2]);

        var pending = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.PendingPayment, pending.GetProperty("status").GetInt32());
        Assert.Equal((int)PaymentProvider.Alipay, pending.GetProperty("payType").GetInt32());

        var pay = await client.PostAsync($"/api/orders/{orderId}/pay", content: null);
        pay.EnsureSuccessStatusCode();
        using var payView = JsonDocument.Parse(await pay.Content.ReadAsStringAsync());
        Assert.Equal($"https://mock-pay.doggo.local/checkout/{created.GetProperty("orderNo").GetString()}", payView.RootElement.GetProperty("payUrl").GetString());
        Assert.Equal(10000, payView.RootElement.GetProperty("amountCents").GetInt64());

        var callback = await PayCallbackAsync(client, orderId, 10000);
        callback.EnsureSuccessStatusCode();
        using var callbackView = JsonDocument.Parse(await callback.Content.ReadAsStringAsync());
        Assert.True(callbackView.RootElement.GetProperty("applied").GetBoolean());

        var paid = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.PendingSettlement, paid.GetProperty("status").GetInt32());
        Assert.NotNull(paid.GetProperty("payTradeNo").GetString());

        var replay = await PayCallbackAsync(client, orderId, 10000);
        replay.EnsureSuccessStatusCode();
        using var replayView = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.False(replayView.RootElement.GetProperty("applied").GetBoolean());

        var cancel = await client.PostAsync($"/api/orders/{orderId}/cancel", content: null);
        cancel.EnsureSuccessStatusCode();
        using var cancelView = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        Assert.False(cancelView.RootElement.GetProperty("cancelled").GetBoolean());

        var refund = await client.PostAsJsonAsync($"/api/orders/{orderId}/refund", new { reason = "customer complaint" });
        refund.EnsureSuccessStatusCode();
        using var refundView = JsonDocument.Parse(await refund.Content.ReadAsStringAsync());
        Assert.True(refundView.RootElement.GetProperty("refunded").GetBoolean());

        var final = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.Refunded, final.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Create_claims_promotion_and_cancel_restores_stock()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        await SeedPromotionAsync(product.Id, stock: 1, discountPriceCents: 7000);
        var client = Factory.CreateClient();

        var created = await CreateOrderAsync(client, product.Id);
        Assert.Equal(7000, created.GetProperty("totalPriceCents").GetInt64());
        Assert.True(created.GetProperty("promotionApplied").GetBoolean());

        var cancel = await client.PostAsync($"/api/orders/{created.GetProperty("orderId").GetGuid()}/cancel", content: null);
        cancel.EnsureSuccessStatusCode();
        using var cancelView = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        Assert.True(cancelView.RootElement.GetProperty("cancelled").GetBoolean());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Equal(1, db.PromotionPlans.Single().Stock);
    }

    [Fact]
    public async Task Failed_callback_keeps_order_pending()
    {
        var product = await SeedCrateAsync();
        var client = Factory.CreateClient();
        var created = await CreateOrderAsync(client, product.Id);

        var callback = await PayCallbackAsync(client, created.GetProperty("orderId").GetGuid(), 10000, success: false);
        callback.EnsureSuccessStatusCode();
        using var callbackView = JsonDocument.Parse(await callback.Content.ReadAsStringAsync());
        Assert.False(callbackView.RootElement.GetProperty("applied").GetBoolean());

        var order = await GetOrderAsync(client, created.GetProperty("orderId").GetGuid());
        Assert.Equal((int)OrderStatus.PendingPayment, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Retrieval_is_rejected_without_vapor_and_order_stays_settled()
    {
        var product = await SeedCrateAsync();
        var client = Factory.CreateClient();
        var created = await CreateOrderAsync(client, product.Id);
        var orderId = created.GetProperty("orderId").GetGuid();
        await PayCallbackAsync(client, orderId, 10000);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var tradeUrlId = Guid.CreateVersion7();
        db.UserTradeUrls.Add(new Doggo.Domain.Users.UserTradeUrl
        {
            Id = tradeUrlId,
            UserId = UserId,
            SteamId = "76561198960265729",
            Partner = "1000000001",
            Token = "tokentoken1",
            Url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1",
            Status = Doggo.Domain.Users.TradeUrlStatus.Valid
        });
        await db.SaveChangesAsync();

        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/retrieval", new
        {
            tradeUrlId,
            itemsToGive = new[] { new { assetId = "12345", appId = "730", classId = "493", instanceId = "0" } }
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        var order = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.PendingSettlement, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Trade_url_crud_and_validate()
    {
        var client = Factory.CreateClient();

        var validate = await client.PostAsJsonAsync("/api/trade-urls/validate", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1" });
        validate.EnsureSuccessStatusCode();
        using var validateView = JsonDocument.Parse(await validate.Content.ReadAsStringAsync());
        Assert.True(validateView.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("76561198960265729", validateView.RootElement.GetProperty("steamId").GetString());

        var add = await client.PostAsJsonAsync($"/api/users/{UserId}/trade-urls", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1", nickname = "primary" });
        add.EnsureSuccessStatusCode();
        using var addView = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var tradeUrlId = addView.RootElement.GetProperty("id").GetGuid();
        Assert.True(addView.RootElement.GetProperty("isPreferred").GetBoolean());

        var duplicate = await client.PostAsJsonAsync($"/api/users/{UserId}/trade-urls", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1" });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await client.GetStringAsync($"/api/users/{UserId}/trade-urls");
        using var listView = JsonDocument.Parse(list);
        Assert.Equal(1, listView.RootElement.EnumerateArray().Count());

        var check = await client.PostAsync($"/api/users/{UserId}/trade-urls/{tradeUrlId}/check", content: null);
        check.EnsureSuccessStatusCode();
        using var checkView = JsonDocument.Parse(await check.Content.ReadAsStringAsync());
        Assert.True(checkView.RootElement.GetProperty("valid").GetBoolean());

        var update = await client.PutAsJsonAsync($"/api/users/{UserId}/trade-urls/{tradeUrlId}", new { nickname = "renamed" });
        update.EnsureSuccessStatusCode();
        using var updateView = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("renamed", updateView.RootElement.GetProperty("nickname").GetString());

        var delete = await client.DeleteAsync($"/api/users/{UserId}/trade-urls/{tradeUrlId}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, delete.StatusCode);

        var listAfterDelete = await client.GetStringAsync($"/api/users/{UserId}/trade-urls");
        using var listAfterView = JsonDocument.Parse(listAfterDelete);
        Assert.Empty(listAfterView.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Order_view_exposes_product_name_icon_and_plan_title()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000, iconUrl: "https://img.example/sna.png");
        var planId = await SeedPlanAsync(product.Id, "双开");
        var client = Factory.CreateClient();

        var created = await CreateOrderAsync(client, product.Id, planId);
        var order = await GetOrderAsync(client, created.GetProperty("orderId").GetGuid());

        Assert.Equal("Snakebite Case", order.GetProperty("productName").GetString());
        Assert.Equal("https://img.example/sna.png", order.GetProperty("productIconUrl").GetString());
        Assert.Equal("双开", order.GetProperty("planTitle").GetString());
    }

    [Fact]
    public async Task Order_endpoints_return_404_for_unknown_orders()
    {
        var client = Factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/api/orders/{id}/pay", content: null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/api/orders/{id}/cancel", content: null)).StatusCode);
    }
}
