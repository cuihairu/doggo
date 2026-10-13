using System.Net;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Analytics;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class OrderServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private sealed class StubHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, Task<HttpResponseMessage>> Responder { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await Responder(request);
        }
    }

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"order-service-tests-{Guid.NewGuid():N}")
            .Options);

    private static (OrderService Orders, StubHandler Handler) CreateService(
        DoggoDbContext db,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? vaporResponder = null)
    {
        var handler = new StubHandler
        {
            Responder = vaporResponder ?? VaporStub()
        };

        var client = new VaporClient(new HttpClient(handler) { BaseAddress = new Uri("http://vapor.test/") }, Options.Create(new VaporOptions
        {
            BaseUrl = "http://vapor.test",
            ApiKey = "key",
            DefaultTarget = "steam1",
            JobPollIntervalMilliseconds = 10,
            JobTimeoutSeconds = 5
        }));

        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        var coupons = new CouponService(db, ledger, NullLogger<CouponService>.Instance);
        return (new OrderService(db, Options.Create(new OrderOptions()), new MockPaymentProvider(), client, coupons, ledger, new InviteService(db, ledger, Options.Create(new InviteOptions()), NullLogger<InviteService>.Instance), NewRecorder(db), NullLogger<OrderService>.Instance), handler);
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string JobJson(Guid jobId, string status, JsonElement? output, string? error) =>
        JsonSerializer.Serialize(new
        {
            id = jobId,
            action = "send_trade_offer",
            state = status,
            createdAt = "2026-10-11T12:00:00Z",
            tasks = new object[]
            {
                new { id = Guid.NewGuid(), action = "send_trade_offer", status, target = "steam1", attempt = 1, output, error }
            }
        });

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> VaporStub(
        string status = "finished",
        string? outputJson = """{"trade_offer_id":"76561198000000001"}""",
        string? error = null,
        List<string>? capturedBodies = null)
    {
        var jobId = Guid.NewGuid();
        var json = JobJson(jobId, status, outputJson is null ? null : JsonDocument.Parse(outputJson).RootElement.Clone(), error);

        return async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                capturedBodies?.Add(await request.Content.ReadAsStringAsync());
                return Response(HttpStatusCode.Accepted, JsonSerializer.Serialize(new { id = jobId }));
            }

            return Response(HttpStatusCode.OK, json);
        };
    }

    private static async Task<Product> SeedProductAsync(
        DoggoDbContext db,
        long finalPriceCents = 10000,
        ProductStatus status = ProductStatus.OnShelf)
    {
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Snakebite Case",
            ClassId = "493",
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

    private static async Task<UserTradeUrl> SeedTradeUrlAsync(DoggoDbContext db, TradeUrlStatus status = TradeUrlStatus.Valid)
    {
        var link = new UserTradeUrl
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            SteamId = "7656119801000000001",
            Partner = "1000000001",
            Token = "tokentoken1",
            Url = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1",
            Nickname = "primary",
            Status = status
        };
        db.UserTradeUrls.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    private static CreateOrderCommand Command(Guid productId, PaymentProvider payType = PaymentProvider.Alipay) =>
        new(UserId, productId, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, payType);

    private static PaymentCallbackResult Callback(Guid orderId, long amountCents = 10000, bool success = true) =>
        new(orderId, $"mock-{orderId}-{amountCents}", $"MOCK-{orderId}", amountCents, success, null);

    private static async Task<UserOrder> SetOrderAsync(DoggoDbContext db, Guid orderId, OrderStatus status, DateTime? payDeadlineUtc = null)
    {
        var order = await db.UserOrders.FirstAsync(o => o.Id == orderId);
        order.Status = status;
        if (payDeadlineUtc is not null)
        {
            order.PayDeadlineUtc = payDeadlineUtc;
        }

        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task Create_uses_final_price_and_pay_deadline()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);

        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.Equal(10000, result.TotalPriceCents);
        Assert.Equal(10000, result.TotalCostCents);
        Assert.Equal(0, result.CashbackCents);
        Assert.False(result.PromotionApplied);
        Assert.Equal("DO", result.OrderNo[..2]);
        Assert.True(result.PayDeadlineUtc > DateTime.UtcNow && result.PayDeadlineUtc <= DateTime.UtcNow.AddSeconds(901));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Null(order.PromotionId);
    }

    [Fact]
    public async Task Create_uses_plan_option_price_and_cashback()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 4000);

        var plan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Title = "5连开",
            MinQuantity = 5,
            MaxQuantity = 5,
            CashbackBps = 500,
            Enabled = true
        };
        var option = new CasePlanOption
        {
            Id = Guid.CreateVersion7(),
            PlanId = plan.Id,
            Quantity = 5,
            PriceCents = 1800,
            Enabled = true
        };
        db.CasePlans.Add(plan);
        db.CasePlanOptions.Add(option);
        await db.SaveChangesAsync();

        var result = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, plan.Id, option.Id, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay));

        Assert.Equal(9000, result.TotalPriceCents);
        Assert.Equal(450, result.CashbackCents);
        Assert.Equal(1800, result.TotalPriceCents / 5);
    }

    [Fact]
    public async Task Create_prices_plain_quantity_through_the_tier_ladder()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);
        db.QuantityOptions.Add(new QuantityOption
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Quantity = 5,
            PriceCents = 9000
        });
        await db.SaveChangesAsync();

        // Below the threshold every unit stays at the base price.
        var below = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay,
            Quantity: 4));
        Assert.Equal(40000, below.TotalPriceCents);

        // Meeting the threshold reprices every unit through the tier.
        var atTier = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay,
            Quantity: 5));
        Assert.Equal(45000, atTier.TotalPriceCents);

        var order = await db.UserOrders.FirstAsync(o => o.TotalPriceCents == 45000);
        Assert.Equal(9000, order.PriceCents);
    }

    [Fact]
    public async Task Create_plan_option_quantity_wins_over_request_quantity()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 4000);
        var plan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Title = "5连开",
            MinQuantity = 5,
            MaxQuantity = 5,
            Enabled = true
        };
        var option = new CasePlanOption
        {
            Id = Guid.CreateVersion7(),
            PlanId = plan.Id,
            Quantity = 5,
            PriceCents = 1800,
            Enabled = true
        };
        db.CasePlans.Add(plan);
        db.CasePlanOptions.Add(option);
        await db.SaveChangesAsync();

        var result = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, plan.Id, option.Id, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay,
            Quantity: 7));

        Assert.Equal(9000, result.TotalPriceCents);
    }

    [Fact]
    public async Task Create_applies_deductions_without_below_zero()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 1000);

        // Real ledger: 500 coins on the balance, one 200-cents-amount-off coupon claimed.
        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        var coupons = new CouponService(db, ledger, NullLogger<CouponService>.Instance);
        await ledger.GrantAsync(UserId, 500, "promo", null);
        var coupon = await db.Coupons.AddAsync(new Coupon
        {
            Id = Guid.CreateVersion7(),
            Title = "Minus 2 yuan",
            Type = CouponType.AmountOff,
            AmountOffCents = 200,
            MinOrderCents = 0,
            Scope = CouponScope.All
        });
        await db.SaveChangesAsync();
        var claimed = await coupons.ClaimCouponAsync(UserId, coupon.Entity.Id);

        var result = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Coin,
            CoinDeduction: 500, UserCouponId: claimed.Id));

        Assert.Equal(1000, result.TotalPriceCents);
        Assert.Equal(300, result.TotalCostCents);

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(500, order.CoinDeduction);
        Assert.Equal(200, order.CouponDeductionCents);
        // The spend drains the whole balance FIFO; the coupon instance is used now.
        Assert.Equal(0, (await db.UserCoins.SingleAsync()).Balance);
        Assert.Equal(UserCouponStatus.Used, (await db.UserCoupons.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_gives_the_coupon_and_coins_back()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 1000);

        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        var coupons = new CouponService(db, ledger, NullLogger<CouponService>.Instance);
        await ledger.GrantAsync(UserId, 500, "promo", null);
        var coupon = await db.Coupons.AddAsync(new Coupon
        {
            Id = Guid.CreateVersion7(),
            Title = "Minus 2 yuan",
            Type = CouponType.AmountOff,
            AmountOffCents = 200,
            MinOrderCents = 0,
            Scope = CouponScope.All
        });
        await db.SaveChangesAsync();
        var claimed = await coupons.ClaimCouponAsync(UserId, coupon.Entity.Id);

        var result = await orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Coin,
            CoinDeduction: 500, UserCouponId: claimed.Id));

        await orders.CancelUnpaidAsync(result.OrderId, "test cancel");

        Assert.Equal(500, (await db.UserCoins.SingleAsync()).Balance);
        var restored = await db.UserCoupons.SingleAsync();
        Assert.Equal(UserCouponStatus.Unused, restored.Status);
        Assert.Null(restored.UsedOrderId);
    }

    [Fact]
    public async Task Create_rejects_a_coupon_from_another_user()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 1000);

        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        var coupons = new CouponService(db, ledger, NullLogger<CouponService>.Instance);
        var coupon = await db.Coupons.AddAsync(new Coupon
        {
            Id = Guid.CreateVersion7(),
            Title = "Minus 2 yuan",
            Type = CouponType.AmountOff,
            AmountOffCents = 200,
            MinOrderCents = 0,
            Scope = CouponScope.All
        });
        await db.SaveChangesAsync();
        var claimed = await coupons.ClaimCouponAsync(Guid.CreateVersion7(), coupon.Entity.Id);

        await Assert.ThrowsAsync<CouponValidationException>(() => orders.CreateOrderAsync(new CreateOrderCommand(
            UserId, product.Id, null, null, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay,
            UserCouponId: claimed.Id)));
    }

    [Fact]
    public async Task Create_claims_promotion_stock_and_prices_discount()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);

        var promotion = new PromotionPlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            DiscountPriceCents = 7000,
            Stock = 2,
            Enabled = true
        };
        db.PromotionPlans.Add(promotion);
        await db.SaveChangesAsync();

        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.Equal(7000, result.TotalPriceCents);
        Assert.Equal(7000, result.TotalCostCents);
        Assert.True(result.PromotionApplied);
        Assert.Equal(promotion.Id, (await db.UserOrders.SingleAsync()).PromotionId);
        Assert.Equal(1, (await db.PromotionPlans.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Create_skips_promotion_past_per_user_limit()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);

        db.PromotionPlans.Add(new PromotionPlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            DiscountPriceCents = 7000,
            Stock = 5,
            PerUserLimit = 1,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var first = await orders.CreateOrderAsync(Command(product.Id));
        var second = await orders.CreateOrderAsync(Command(product.Id));

        Assert.Equal(7000, first.TotalPriceCents);
        Assert.True(first.PromotionApplied);
        Assert.Equal(10000, second.TotalPriceCents);
        Assert.False(second.PromotionApplied);
        Assert.Equal(4, (await db.PromotionPlans.SingleAsync()).Stock);
    }

    [Fact]
    public async Task Create_skips_promotion_outside_window_or_without_stock()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);

        db.PromotionPlans.Add(new PromotionPlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            DiscountPriceCents = 7000,
            Stock = 1,
            Enabled = true,
            StartAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(result.PromotionApplied);
        Assert.Equal(10000, result.TotalPriceCents);
    }

    [Theory]
    [InlineData(ProductStatus.SelfOpenOnly, ServiceMode.CloudOpen, "self-open")]
    [InlineData(ProductStatus.GroupOpenOnly, ServiceMode.SelfOpen, "cloud-open")]
    public async Task Create_rejects_service_mode_mismatch(ProductStatus status, ServiceMode mode, string expectedFragment)
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, status: status);
        var command = new CreateOrderCommand(UserId, product.Id, null, null, OrderEntry.Case, mode, FulfillmentType.Spot, PaymentProvider.Alipay);

        var ex = await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(command));
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Theory]
    [InlineData(ProductStatus.SelfOpenOnly, ServiceMode.SelfOpen)]
    [InlineData(ProductStatus.GroupOpenOnly, ServiceMode.CloudOpen)]
    [InlineData(ProductStatus.OnShelf, ServiceMode.CloudOpen)]
    public async Task Create_allows_matching_service_modes(ProductStatus status, ServiceMode mode)
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, status: status);

        var result = await orders.CreateOrderAsync(new CreateOrderCommand(UserId, product.Id, null, null, OrderEntry.Case, mode, FulfillmentType.Spot, PaymentProvider.Alipay));

        Assert.Equal(10000, result.TotalPriceCents);
    }

    [Fact]
    public async Task Create_rejects_off_shelf_products()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, status: ProductStatus.OffShelf);

        var ex = await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(Command(product.Id)));
        Assert.Contains("off shelf", ex.Message);
    }

    [Fact]
    public async Task Create_rejects_plan_option_from_another_product()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var foreignProduct = await SeedProductAsync(db);

        var foreignPlan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = foreignProduct.Id,
            Title = "foreign plan",
            Enabled = true
        };
        var option = new CasePlanOption
        {
            Id = Guid.CreateVersion7(),
            PlanId = foreignPlan.Id,
            Quantity = 1,
            PriceCents = 5000,
            Enabled = true
        };
        db.CasePlans.Add(foreignPlan);
        db.CasePlanOptions.Add(option);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(
            new CreateOrderCommand(UserId, product.Id, option.PlanId, option.Id, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay)));
    }

    [Fact]
    public async Task Create_rejects_missing_or_disabled_plan_option()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var plan = new CasePlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Title = "plan",
            Enabled = true
        };
        var option = new CasePlanOption
        {
            Id = Guid.CreateVersion7(),
            PlanId = plan.Id,
            Quantity = 1,
            PriceCents = 5000,
            Enabled = true
        };
        db.CasePlans.Add(plan);
        db.CasePlanOptions.Add(option);
        await db.SaveChangesAsync();

        var detached = new CreateOrderCommand(UserId, product.Id, plan.Id, Guid.CreateVersion7(), OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay);
        await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(detached));

        option.Enabled = false;
        await db.SaveChangesAsync();
        var disabled = new CreateOrderCommand(UserId, product.Id, plan.Id, option.Id, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay);
        await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(disabled));

        var noPlan = new CreateOrderCommand(UserId, product.Id, null, option.Id, OrderEntry.Case, ServiceMode.CloudOpen, FulfillmentType.Spot, PaymentProvider.Alipay);
        await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreateOrderAsync(noPlan));
    }

    [Fact]
    public async Task CreatePayment_returns_provider_url_for_unpaid_orders_only()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 5000);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        var creation = await orders.CreatePaymentAsync(result.OrderId);
        Assert.Equal($"https://mock-pay.doggo.local/checkout/{result.OrderNo}", creation.PayUrl);
        Assert.Equal(5000, creation.AmountCents);

        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId, 5000), null);
        await Assert.ThrowsAsync<OrderValidationException>(() => orders.CreatePaymentAsync(result.OrderId));
    }

    [Fact]
    public async Task PaymentCallback_is_idempotent_and_moves_order_once()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 10000);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.True(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), "{payload}"));
        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), "{payload}"));
        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), "{payload}"));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.PendingSettlement, order.Status);
        Assert.Equal(10000, order.TotalCostCents);
        Assert.NotNull(order.PaidAtUtc);
        Assert.Equal(PaymentProvider.Alipay, order.PayType);
        Assert.Equal(1, await db.PaymentRecords.CountAsync());
        Assert.Equal(PaymentStatus.Success, (await db.PaymentRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task PaymentCallback_rejected_after_cancel_keeps_cancelled()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.True(await orders.CancelUnpaidAsync(result.OrderId, "cancelled by user"));
        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Failed, (await db.PaymentRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task PaymentCallback_rejected_for_failed_result()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId, 500, success: false), null));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(1, await db.PaymentRecords.CountAsync());
    }

    [Fact]
    public async Task CancelUnpaid_returns_promotion_stock()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);

        db.PromotionPlans.Add(new PromotionPlan
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            DiscountPriceCents = 7000,
            Stock = 3,
            Enabled = true
        });
        await db.SaveChangesAsync();

        var result = await orders.CreateOrderAsync(Command(product.Id));
        Assert.Equal(2, (await db.PromotionPlans.SingleAsync()).Stock);

        Assert.True(await orders.CancelUnpaidAsync(result.OrderId, "user gave up"));
        Assert.Equal(3, (await db.PromotionPlans.SingleAsync()).Stock);
        Assert.Equal(OrderStatus.Cancelled, (await db.UserOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task CancelUnpaid_rejects_paid_orders()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        Assert.False(await orders.CancelUnpaidAsync(result.OrderId, "too late"));
    }

    [Fact]
    public async Task ExpireUnpaid_only_cancels_deadline_passed_orders()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);

        var overdue = await orders.CreateOrderAsync(Command(product.Id));
        var fresh = await orders.CreateOrderAsync(Command(product.Id));
        var openEnded = await orders.CreateOrderAsync(Command(product.Id));

        await SetOrderAsync(db, overdue.OrderId, OrderStatus.PendingPayment, payDeadlineUtc: DateTime.UtcNow.AddHours(-1));
        await SetOrderAsync(db, fresh.OrderId, OrderStatus.PendingPayment, payDeadlineUtc: DateTime.UtcNow.AddHours(1));
        await SetOrderAsync(db, openEnded.OrderId, OrderStatus.PendingPayment, payDeadlineUtc: null);

        Assert.Equal(1, await orders.ExpireUnpaidOrdersAsync());

        Assert.Equal(OrderStatus.Cancelled, (await db.UserOrders.SingleAsync(o => o.Id == overdue.OrderId)).Status);
        Assert.Equal(OrderStatus.PendingPayment, (await db.UserOrders.SingleAsync(o => o.Id == fresh.OrderId)).Status);
        Assert.Equal(OrderStatus.PendingPayment, (await db.UserOrders.SingleAsync(o => o.Id == openEnded.OrderId)).Status);
    }

    [Fact]
    public async Task RefundOrder_calls_provider_and_records_refund()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db, finalPriceCents: 9000);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId, 9000), null);

        Assert.True(await orders.RefundOrderAsync(result.OrderId, "customer complaint"));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.Refunded, order.Status);
        var record = await db.RefundRecords.SingleAsync();
        Assert.Equal(PaymentProvider.Alipay, record.Provider);
        Assert.Equal(9000, record.AmountCents);
        Assert.Equal("customer complaint", record.Reason);
        Assert.Equal(PaymentStatus.Refunded, record.Status);
    }

    [Fact]
    public async Task RefundOrder_rejects_unpaid_and_terminal_orders()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(await orders.RefundOrderAsync(result.OrderId, "too early"));
        Assert.True(await orders.CancelUnpaidAsync(result.OrderId, "gone"));
        Assert.False(await orders.RefundOrderAsync(result.OrderId, "again"));
    }

    [Fact]
    public async Task RefundStuck_only_touches_aged_orders()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        Assert.Equal(0, await orders.RefundStuckOrdersAsync());
        Assert.Equal(1, await orders.RefundStuckOrdersAsync(thresholdUtc: DateTime.UtcNow));

        Assert.Equal(OrderStatus.Refunded, (await db.UserOrders.SingleAsync()).Status);
        Assert.Equal(1, await db.RefundRecords.CountAsync());
    }

    [Fact]
    public async Task RollbackStuckRetrievals_only_touches_retrieving_orders_beyond_threshold()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);

        var retrieving = (await orders.CreateOrderAsync(Command(product.Id))).OrderId;
        var settled = (await orders.CreateOrderAsync(Command(product.Id))).OrderId;

        await SetOrderAsync(db, retrieving, OrderStatus.Retrieving);
        await SetOrderAsync(db, settled, OrderStatus.PendingSettlement);

        // Fresh rows are younger than the default 120 minute threshold.
        Assert.Equal(0, await orders.RollbackStuckRetrievalsAsync());
        Assert.Equal(OrderStatus.Retrieving, (await db.UserOrders.FirstAsync(o => o.Id == retrieving)).Status);

        // A threshold that has already passed rolls the retrieval back but leaves settlement alone.
        Assert.Equal(1, await orders.RollbackStuckRetrievalsAsync(thresholdUtc: DateTime.UtcNow));

        var rolled = await db.UserOrders.FirstAsync(o => o.Id == retrieving);
        Assert.Equal(OrderStatus.PendingRetrieval, rolled.Status);
        Assert.Contains("rolled back", rolled.Error);
        Assert.Equal(OrderStatus.PendingSettlement, (await db.UserOrders.FirstAsync(o => o.Id == settled)).Status);
    }

    [Fact]
    public async Task SubmitRetrieval_dispatches_vapor_and_records_offer_id()
    {
        using var db = CreateDb();
        var captured = new List<string>();
        var (orders, _) = CreateService(db, VaporStub(capturedBodies: captured));
        var product = await SeedProductAsync(db);
        var link = await SeedTradeUrlAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        var (accepted, offerId, error) = await orders.SubmitRetrievalAsync(result.OrderId, link.Id,
        [
            new VaporTradeAsset("12345", "730", "493", "0")
        ]);

        Assert.True(accepted);
        Assert.Equal("76561198000000001", offerId);
        Assert.Null(error);

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.Retrieving, order.Status);
        Assert.Equal(link.Id, order.TradeUrlId);
        Assert.Equal("76561198000000001", order.TradeOfferId);

        var dispatched = Assert.Single(captured);
        Assert.Contains("send_trade_offer", dispatched);
        Assert.Contains("trade_url", dispatched);
        Assert.Contains("items_to_give", dispatched);
        // vapor reads items_to_give entry keys with an exact dictionary lookup:
        // assetid/classid spellings would be silently dropped.
        Assert.Contains("\"asset_id\":\"12345\"", dispatched);
        Assert.Contains("\"app_id\":\"730\"", dispatched);
        Assert.DoesNotContain("assetid", dispatched);
        Assert.DoesNotContain("classid", dispatched);
        Assert.Contains("steam1", dispatched);
    }

    [Fact]
    public async Task SubmitRetrieval_rejects_when_vapor_is_unconfigured()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db, VaporStub());
        var unconfigured = new VaporClient(new HttpClient(new StubHandler { Responder = VaporStub() }) { BaseAddress = new Uri("http://vapor.test/") }, Options.Create(new VaporOptions()));
        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        var plain = new OrderService(db, Options.Create(new OrderOptions()), new MockPaymentProvider(), unconfigured, new CouponService(db, ledger, NullLogger<CouponService>.Instance), ledger, new InviteService(db, ledger, Options.Create(new InviteOptions()), NullLogger<InviteService>.Instance), NewRecorder(db), NullLogger<OrderService>.Instance);

        var product = await SeedProductAsync(db);
        var link = await SeedTradeUrlAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        var (accepted, offerId, error) = await plain.SubmitRetrievalAsync(result.OrderId, link.Id, []);

        Assert.False(accepted);
        Assert.Null(offerId);
        Assert.Contains("vapor is not configured", error);
        Assert.Equal(OrderStatus.PendingSettlement, (await db.UserOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task SubmitRetrieval_rejects_invalid_trade_url_without_touching_order()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var link = await SeedTradeUrlAsync(db, TradeUrlStatus.Invalid);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        var (accepted, _, error) = await orders.SubmitRetrievalAsync(result.OrderId, link.Id, []);

        Assert.False(accepted);
        Assert.Contains("validation", error);
        Assert.Equal(OrderStatus.PendingSettlement, (await db.UserOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task SubmitRetrieval_rolls_back_when_vapor_task_fails()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db, VaporStub(status: "failed", outputJson: null, error: "trade server offline"));
        var product = await SeedProductAsync(db);
        var link = await SeedTradeUrlAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        var (accepted, _, error) = await orders.SubmitRetrievalAsync(result.OrderId, link.Id, []);

        Assert.False(accepted);
        Assert.Contains("trade server offline", error);

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.PendingRetrieval, order.Status);
        Assert.Contains("trade server offline", order.Error);
    }

    [Fact]
    public async Task SubmitRetrieval_rolls_back_when_offer_id_is_missing()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db, VaporStub(outputJson: """{"unexpected":true}"""));
        var product = await SeedProductAsync(db);
        var link = await SeedTradeUrlAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));
        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);

        var (accepted, _, error) = await orders.SubmitRetrievalAsync(result.OrderId, link.Id, []);

        Assert.False(accepted);
        Assert.Contains("no trade offer id", error);
        Assert.Equal(OrderStatus.PendingRetrieval, (await db.UserOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task CompleteRetrieval_only_from_retrieving()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(await orders.CompleteRetrievalAsync(result.OrderId, "offer-1"));

        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);
        await SetOrderAsync(db, result.OrderId, OrderStatus.Retrieving);

        Assert.True(await orders.CompleteRetrievalAsync(result.OrderId, "offer-2"));
        Assert.False(await orders.CompleteRetrievalAsync(result.OrderId, "offer-2"));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal("offer-2", order.TradeOfferId);
        Assert.NotNull(order.CompletedAtUtc);
    }

    [Fact]
    public async Task FailRetrieval_only_from_retrieving()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(await orders.FailRetrievalAsync(result.OrderId, "offer declined"));

        await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null);
        await SetOrderAsync(db, result.OrderId, OrderStatus.Retrieving);

        Assert.True(await orders.FailRetrievalAsync(result.OrderId, "offer declined"));
        Assert.False(await orders.FailRetrievalAsync(result.OrderId, "offer declined"));

        var order = await db.UserOrders.SingleAsync();
        Assert.Equal(OrderStatus.PendingRetrieval, order.Status);
        Assert.Equal("offer declined", order.Error);
    }

    [Fact]
    public async Task FindOrderAsync_throws_for_missing_orders() =>
        await Assert.ThrowsAsync<OrderNotFoundException>(() =>
        {
            using var db = CreateDb();
            var (orders, _) = CreateService(db);
            return orders.FindOrderAsync(Guid.CreateVersion7());
        });

    // ---- funnel events (server-authoritative, design §6) ----

    private static Doggo.Infrastructure.Analytics.AnalyticsRecorder NewRecorder(DoggoDbContext db) =>
        new(db, new Microsoft.AspNetCore.Http.HttpContextAccessor(), NullLogger<Doggo.Infrastructure.Analytics.AnalyticsRecorder>.Instance);

    [Fact]
    public async Task Create_order_writes_the_order_created_event()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);

        var result = await orders.CreateOrderAsync(Command(product.Id));

        var evt = await db.AnalyticsEvents.SingleAsync(e => e.EventName == "order_created");
        Assert.Equal(UserId, evt.UserId);
        var props = JsonDocument.Parse(evt.PropertiesJson).RootElement;
        Assert.Equal(result.OrderId.ToString(), props.GetProperty("orderId").GetString());
        Assert.Equal(product.Id.ToString(), props.GetProperty("productId").GetString());
        Assert.Equal(result.TotalPriceCents, props.GetProperty("totalPriceCents").GetInt64());
    }

    [Fact]
    public async Task Successful_callback_writes_the_order_paid_event_once()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.True(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId), null));
        // Duplicate delivery: idempotent, no second event.
        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId, amountCents: 10000), null));

        var events = await db.AnalyticsEvents.Where(e => e.EventName == "order_paid").ToListAsync();
        var evt = Assert.Single(events);
        Assert.Equal(UserId, evt.UserId);
        var props = JsonDocument.Parse(evt.PropertiesJson).RootElement;
        Assert.Equal(result.OrderId.ToString(), props.GetProperty("orderId").GetString());
        Assert.Equal(10000, props.GetProperty("amountCents").GetInt64());
    }

    [Fact]
    public async Task Failed_callback_writes_no_order_paid_event()
    {
        using var db = CreateDb();
        var (orders, _) = CreateService(db);
        var product = await SeedProductAsync(db);
        var result = await orders.CreateOrderAsync(Command(product.Id));

        Assert.False(await orders.ApplyPaymentCallbackAsync(result.OrderId, Callback(result.OrderId, success: false), null));

        Assert.False(await db.AnalyticsEvents.AnyAsync(e => e.EventName == "order_paid"));
    }
}
