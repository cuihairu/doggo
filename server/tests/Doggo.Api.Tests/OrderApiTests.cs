using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class OrderApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    /// <summary>Registers a user and attaches its bearer token; the order endpoints require it.</summary>
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

    private async Task<Guid> SeedPlanAsync(Guid productId, string title, bool canUseCoin = false, bool canUseCoupon = false)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var plan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = productId,
            Title = title,
            CanUseCoin = canUseCoin,
            CanUseCoupon = canUseCoupon
        };
        db.CasePlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private async Task SeedCoinsAsync(Guid userId, long balance)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.UserCoins.Add(new Doggo.Domain.Users.UserCoin { Id = Guid.CreateVersion7(), UserId = userId, Balance = balance });
        db.UserCoinBatches.Add(new Doggo.Domain.Users.UserCoinBatch
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OriginalAmount = balance,
            RemainingAmount = balance,
            SourceType = "test"
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCouponAsync(Guid userId, long amountOffCents = 200)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var coupon = new Doggo.Domain.Users.Coupon
        {
            Id = Guid.CreateVersion7(),
            Title = "Minus 2 yuan",
            Type = Doggo.Domain.Users.CouponType.AmountOff,
            AmountOffCents = amountOffCents,
            MinOrderCents = 0,
            Scope = Doggo.Domain.Users.CouponScope.All
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        var owned = new Doggo.Domain.Users.UserCoupon
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CouponId = coupon.Id,
            Status = Doggo.Domain.Users.UserCouponStatus.Unused
        };
        db.UserCoupons.Add(owned);
        await db.SaveChangesAsync();
        return owned.Id;
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

    private static async Task<JsonElement> CreateOrderAsync(HttpClient client, Guid userId, Guid productId, Guid? planId = null, long coinDeduction = 0, Guid? userCouponId = null)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            productId,
            planId,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay,
            coinDeduction,
            userCouponId
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
        var (client, userId) = await AuthorizedAsync();

        var created = await CreateOrderAsync(client, userId, product.Id);
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
        var (client, userId) = await AuthorizedAsync();

        var created = await CreateOrderAsync(client, userId, product.Id);
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
        var (client, userId) = await AuthorizedAsync();
        var created = await CreateOrderAsync(client, userId, product.Id);

        var callback = await PayCallbackAsync(client, created.GetProperty("orderId").GetGuid(), 10000, success: false);
        callback.EnsureSuccessStatusCode();
        using var callbackView = JsonDocument.Parse(await callback.Content.ReadAsStringAsync());
        Assert.False(callbackView.RootElement.GetProperty("applied").GetBoolean());

        var order = await GetOrderAsync(client, created.GetProperty("orderId").GetGuid());
        Assert.Equal((int)OrderStatus.PendingPayment, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Amount_mismatch_callback_is_rejected_and_kept_pending()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var (client, userId) = await AuthorizedAsync();
        var created = await CreateOrderAsync(client, userId, product.Id);
        var orderId = created.GetProperty("orderId").GetGuid();

        var callback = await PayCallbackAsync(client, orderId, 9999);
        callback.EnsureSuccessStatusCode();
        using var callbackView = JsonDocument.Parse(await callback.Content.ReadAsStringAsync());
        Assert.False(callbackView.RootElement.GetProperty("applied").GetBoolean());

        var order = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.PendingPayment, order.GetProperty("status").GetInt32());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var record = Assert.Single(db.PaymentRecords, record => record.OrderId == orderId);
        Assert.Equal(9999, record.AmountCents);
        Assert.Equal(PaymentStatus.Failed, record.Status);
    }

    [Fact]
    public async Task Retrieval_is_rejected_without_vapor_and_order_stays_settled()
    {
        var product = await SeedCrateAsync();
        var (client, userId) = await AuthorizedAsync();
        var created = await CreateOrderAsync(client, userId, product.Id);
        var orderId = created.GetProperty("orderId").GetGuid();
        await PayCallbackAsync(client, orderId, 10000);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var tradeUrlId = Guid.CreateVersion7();
        db.UserTradeUrls.Add(new Doggo.Domain.Users.UserTradeUrl
        {
            Id = tradeUrlId,
            UserId = userId,
            SteamId = "76561198960265729",
            Partner = "1000000001",
            Token = "tokentoken1",
            Url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1",
            Status = Doggo.Domain.Users.TradeUrlStatus.Valid
        });
        await db.SaveChangesAsync();

        // The client no longer supplies items: the offer is built from the order's prizes
        // server-side, so the request carries only the destination trade url.
        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/retrieval", new
        {
            tradeUrlId
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        var order = await GetOrderAsync(client, orderId);
        Assert.Equal((int)OrderStatus.PendingSettlement, order.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Trade_url_crud_and_validate()
    {
        var (client, userId) = await AuthorizedAsync();

        var validate = await client.PostAsJsonAsync("/api/trade-urls/validate", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1" });
        validate.EnsureSuccessStatusCode();
        using var validateView = JsonDocument.Parse(await validate.Content.ReadAsStringAsync());
        Assert.True(validateView.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal("76561198960265729", validateView.RootElement.GetProperty("steamId").GetString());

        var add = await client.PostAsJsonAsync($"/api/users/{userId}/trade-urls", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1", nickname = "primary" });
        add.EnsureSuccessStatusCode();
        using var addView = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var tradeUrlId = addView.RootElement.GetProperty("id").GetGuid();
        Assert.True(addView.RootElement.GetProperty("isPreferred").GetBoolean());

        var duplicate = await client.PostAsJsonAsync($"/api/users/{userId}/trade-urls", new { url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1" });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await client.GetStringAsync($"/api/users/{userId}/trade-urls");
        using var listView = JsonDocument.Parse(list);
        Assert.Equal(1, listView.RootElement.EnumerateArray().Count());

        var check = await client.PostAsync($"/api/users/{userId}/trade-urls/{tradeUrlId}/check", content: null);
        check.EnsureSuccessStatusCode();
        using var checkView = JsonDocument.Parse(await check.Content.ReadAsStringAsync());
        Assert.True(checkView.RootElement.GetProperty("valid").GetBoolean());

        var update = await client.PutAsJsonAsync($"/api/users/{userId}/trade-urls/{tradeUrlId}", new { nickname = "renamed" });
        update.EnsureSuccessStatusCode();
        using var updateView = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
        Assert.Equal("renamed", updateView.RootElement.GetProperty("nickname").GetString());

        var delete = await client.DeleteAsync($"/api/users/{userId}/trade-urls/{tradeUrlId}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, delete.StatusCode);

        var listAfterDelete = await client.GetStringAsync($"/api/users/{userId}/trade-urls");
        using var listAfterView = JsonDocument.Parse(listAfterDelete);
        Assert.Empty(listAfterView.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Order_view_exposes_product_name_icon_and_plan_title()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000, iconUrl: "https://img.example/sna.png");
        var planId = await SeedPlanAsync(product.Id, "双开");
        var (client, userId) = await AuthorizedAsync();

        var created = await CreateOrderAsync(client, userId, product.Id, planId);
        var order = await GetOrderAsync(client, created.GetProperty("orderId").GetGuid());

        Assert.Equal("Snakebite Case", order.GetProperty("productName").GetString());
        Assert.Equal("https://img.example/sna.png", order.GetProperty("productIconUrl").GetString());
        Assert.Equal("双开", order.GetProperty("planTitle").GetString());
    }

    [Fact]
    public async Task Order_endpoints_return_404_for_unknown_orders()
    {
        var (client, _) = await AuthorizedAsync();
        var id = Guid.NewGuid();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/api/orders/{id}/pay", content: null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/api/orders/{id}/cancel", content: null)).StatusCode);
    }

    [Fact]
    public async Task Coin_deduction_is_rejected_when_the_plan_disallows_coins()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var planId = await SeedPlanAsync(product.Id, "no-coin", canUseCoin: false);
        var (client, userId) = await AuthorizedAsync();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            productId = product.Id,
            planId,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay,
            coinDeduction = 1000
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Coupon_is_rejected_when_the_plan_disallows_coupons()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var planId = await SeedPlanAsync(product.Id, "no-coupon", canUseCoupon: false);
        var (client, userId) = await AuthorizedAsync();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            productId = product.Id,
            planId,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay,
            userCouponId = Guid.NewGuid()
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Coin_deduction_above_the_order_total_is_rejected()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var (client, userId) = await AuthorizedAsync();
        await SeedCoinsAsync(userId, balance: 100000);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            productId = product.Id,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay,
            coinDeduction = 20000 // above the 10000 total
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        // The cap rejected before the spend; the balance must be untouched and no order row saved.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Equal(100000, Assert.Single(await db.UserCoins.Where(c => c.UserId == userId).ToListAsync()).Balance);
        Assert.Empty(db.UserOrders);
    }

    [Fact]
    public async Task Coin_deduction_within_the_total_is_applied()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var planId = await SeedPlanAsync(product.Id, "coin-ok", canUseCoin: true);
        var (client, userId) = await AuthorizedAsync();
        await SeedCoinsAsync(userId, balance: 100000);

        var created = await CreateOrderAsync(client, userId, product.Id, planId, coinDeduction: 2000);
        var order = await GetOrderAsync(client, created.GetProperty("orderId").GetGuid());

        Assert.Equal(2000, order.GetProperty("coinDeduction").GetInt64());
        Assert.Equal(8000, order.GetProperty("totalCostCents").GetInt64());

        // The coin ledger must actually shrink by the deduction.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Equal(98000, Assert.Single(await db.UserCoins.Where(c => c.UserId == userId).ToListAsync()).Balance);
    }

    [Fact]
    public async Task Rejection_after_coupon_consumption_restores_the_coupon_and_leaves_no_order()
    {
        var product = await SeedCrateAsync(finalPriceCents: 10000);
        var (client, userId) = await AuthorizedAsync();
        // No coin balance: the cap passes (200 < 10000) but SpendAsync fails on balance.
        var userCouponId = await SeedCouponAsync(userId, amountOffCents: 200);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            productId = product.Id,
            entry = (int)OrderEntry.Case,
            serviceMode = (int)ServiceMode.CloudOpen,
            fulfillmentType = (int)FulfillmentType.Spot,
            payType = (int)PaymentProvider.Alipay,
            coinDeduction = 200,
            userCouponId
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var restored = Assert.Single(await db.UserCoupons.Where(c => c.Id == userCouponId).ToListAsync());
        Assert.Equal(Doggo.Domain.Users.UserCouponStatus.Unused, restored.Status);
        Assert.Null(restored.UsedOrderId);
        Assert.Empty(db.UserOrders);
    }
}
