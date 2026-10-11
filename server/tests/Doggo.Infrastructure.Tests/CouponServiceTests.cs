using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Doggo.Infrastructure.Tests;

public class CouponServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"coupon-service-tests-{Guid.NewGuid():N}")
            .Options);

    private static (CouponService Coupons, CoinService Ledger) CreateServices(DoggoDbContext db)
    {
        var ledger = new CoinService(db, NullLogger<CoinService>.Instance);
        return (new CouponService(db, ledger, NullLogger<CouponService>.Instance), ledger);
    }

    private static async Task<RedeemCode> SeedCoinCodeAsync(DoggoDbContext db, long amount = 100, string code = "COIN100")
    {
        var redeemCode = new RedeemCode
        {
            Id = Guid.CreateVersion7(),
            Type = RedeemCodeType.Coin,
            Code = code,
            BatchNo = "B1",
            CoinAmount = amount,
            MaxRedemptions = 1
        };
        db.RedeemCodes.Add(redeemCode);
        await db.SaveChangesAsync();
        return redeemCode;
    }

    private static async Task<Coupon> SeedCouponAsync(DoggoDbContext db, Action<Coupon>? setup = null)
    {
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            Title = "Minus 2 yuan",
            Type = CouponType.AmountOff,
            AmountOffCents = 200,
            MinOrderCents = 0,
            Scope = CouponScope.All
        };
        setup?.Invoke(coupon);
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        return coupon;
    }

    [Fact]
    public async Task Redeem_coin_code_credits_the_ledger_and_counts_once()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var redeemCode = await SeedCoinCodeAsync(db);

        var result = await coupons.RedeemAsync(UserId, "coin100");

        Assert.Equal(RedeemCodeType.Coin, result.Type);
        Assert.Equal(100, result.CoinAmount);
        Assert.Equal(100, result.Balance);
        Assert.Equal(1, (await db.RedeemCodes.SingleAsync()).RedeemedCount);
        Assert.Single(await db.UserRedeemRecords.ToListAsync());

        // A second redemption by the same user is refused even though the count allows it.
        (await db.RedeemCodes.SingleAsync()).MaxRedemptions = 5;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.RedeemAsync(UserId, "coin100"));
    }

    [Fact]
    public async Task Redeem_rejects_unknown_disabled_and_exhausted_codes()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);

        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.RedeemAsync(UserId, "NOPE"));

        var disabled = await SeedCoinCodeAsync(db, code: "DISABLED");
        disabled.Status = RedeemCodeStatus.Disabled;
        var exhausted = await SeedCoinCodeAsync(db, code: "EMPTY");
        exhausted.RedeemedCount = exhausted.MaxRedemptions;
        var expired = await SeedCoinCodeAsync(db, code: "OLD");
        expired.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.RedeemAsync(UserId, "disabled"));
        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.RedeemAsync(UserId, "EMPTY"));
        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.RedeemAsync(UserId, "OLD"));
        Assert.Equal(RedeemCodeStatus.Expired, (await db.RedeemCodes.SingleAsync(c => c.Code == "OLD")).Status);
    }

    [Fact]
    public async Task Redeem_coupon_code_issues_a_coupon_instance()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var coupon = await SeedCouponAsync(db);
        var redeemCode = new RedeemCode
        {
            Id = Guid.CreateVersion7(),
            Type = RedeemCodeType.Coupon,
            Code = "CPN1",
            BatchNo = "B1",
            CouponId = coupon.Id,
            MaxRedemptions = 1
        };
        db.RedeemCodes.Add(redeemCode);
        await db.SaveChangesAsync();

        var result = await coupons.RedeemAsync(UserId, "cpn1");

        Assert.Equal(RedeemCodeType.Coupon, result.Type);
        Assert.Equal(coupon.Id, result.CouponId);
        var issued = await db.UserCoupons.SingleAsync();
        Assert.Equal(UserCouponStatus.Unused, issued.Status);
        Assert.Equal(coupon.ValidToUtc, issued.ValidToUtc);
    }

    [Fact]
    public async Task Claim_respects_per_user_and_total_limits()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var coupon = await SeedCouponAsync(db, c =>
        {
            c.PerUserLimit = 1;
            c.TotalLimit = 2;
        });

        await coupons.ClaimCouponAsync(UserId, coupon.Id);
        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.ClaimCouponAsync(UserId, coupon.Id));

        var other = Guid.CreateVersion7();
        await coupons.ClaimCouponAsync(other, coupon.Id);

        // Total limit of 2 is now reached.
        await Assert.ThrowsAsync<CouponValidationException>(() => coupons.ClaimCouponAsync(Guid.CreateVersion7(), coupon.Id));
    }

    [Fact]
    public async Task Apply_rejects_foreign_used_expired_and_out_of_scope_coupons()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            Type = ProductType.Crate,
            Name = "Case A",
            ClassId = "c1",
            Status = ProductStatus.OnShelf,
            FinalPriceCents = 1000,
            RegularPriceCents = 1200
        };
        db.Products.Add(product);
        var scoped = await SeedCouponAsync(db, c =>
        {
            c.Scope = CouponScope.Products;
        });
        db.CouponProducts.Add(new CouponProduct { CouponId = scoped.Id, ProductId = product.Id });
        await db.SaveChangesAsync();
        var claimed = await coupons.ClaimCouponAsync(UserId, scoped.Id);

        // Another user's instance is not usable.
        await Assert.ThrowsAsync<CouponValidationException>(
            () => coupons.ApplyCouponAsync(Guid.CreateVersion7(), claimed.Id, product.Id, 1000, Guid.CreateVersion7()));

        // A different product outside the scope is refused.
        await Assert.ThrowsAsync<CouponValidationException>(
            () => coupons.ApplyCouponAsync(UserId, claimed.Id, Guid.CreateVersion7(), 1000, Guid.CreateVersion7()));

        // Minimum order applies.
        var withMin = await SeedCouponAsync(db, c => c.MinOrderCents = 5000);
        var minClaimed = await coupons.ClaimCouponAsync(UserId, withMin.Id);
        await Assert.ThrowsAsync<CouponValidationException>(
            () => coupons.ApplyCouponAsync(UserId, minClaimed.Id, product.Id, 1000, Guid.CreateVersion7()));

        // After one successful apply, the instance cannot be used again.
        var orderId = Guid.CreateVersion7();
        Assert.Equal(200, await coupons.ApplyCouponAsync(UserId, claimed.Id, product.Id, 1000, orderId));
        await Assert.ThrowsAsync<CouponValidationException>(
            () => coupons.ApplyCouponAsync(UserId, claimed.Id, product.Id, 1000, Guid.CreateVersion7()));
        Assert.Equal(UserCouponStatus.Used, (await db.UserCoupons.SingleAsync(uc => uc.Id == claimed.Id)).Status);
    }

    [Fact]
    public async Task Apply_percent_off_covers_the_order_total()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var coupon = await SeedCouponAsync(db, c =>
        {
            c.Type = CouponType.PercentOff;
            c.PercentOff = 30;
        });
        var claimed = await coupons.ClaimCouponAsync(UserId, coupon.Id);

        var deduction = await coupons.ApplyCouponAsync(UserId, claimed.Id, Guid.CreateVersion7(), 1000, Guid.CreateVersion7());

        Assert.Equal(300, deduction);
    }

    [Fact]
    public async Task Restore_is_idempotent_and_reopens_the_instance()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var coupon = await SeedCouponAsync(db);
        var claimed = await coupons.ClaimCouponAsync(UserId, coupon.Id);
        var orderId = Guid.CreateVersion7();
        await coupons.ApplyCouponAsync(UserId, claimed.Id, Guid.CreateVersion7(), 1000, orderId);

        await coupons.RestoreCouponAsync(orderId);
        await coupons.RestoreCouponAsync(orderId);

        var restored = await db.UserCoupons.SingleAsync(uc => uc.Id == claimed.Id);
        Assert.Equal(UserCouponStatus.Unused, restored.Status);
        Assert.Null(restored.UsedOrderId);
    }

    [Fact]
    public async Task List_shows_the_user_instances_only()
    {
        await using var db = CreateDb();
        var (coupons, _) = CreateServices(db);
        var coupon = await SeedCouponAsync(db);
        await coupons.ClaimCouponAsync(UserId, coupon.Id);
        await coupons.ClaimCouponAsync(Guid.CreateVersion7(), coupon.Id);

        var mine = await coupons.ListMyCouponsAsync(UserId);

        Assert.Single(mine);
        Assert.Equal(coupon.Title, mine[0].Title);
        Assert.Equal(UserCouponStatus.Unused, mine[0].Status);
    }
}
