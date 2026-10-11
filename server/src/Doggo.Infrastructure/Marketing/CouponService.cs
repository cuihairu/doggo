using Doggo.Domain.Products;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Marketing;

/// <summary>Raised when a redeem code or coupon is unusable — maps to 400.</summary>
public sealed class CouponValidationException(string message) : Exception(message)
{
}

public sealed record RedeemResult(
    RedeemCodeType Type,
    long CoinAmount,
    Guid? CouponId,
    string CouponTitle,
    long Balance);

public sealed record CouponView(
    Guid Id,
    Guid CouponId,
    string Title,
    CouponType Type,
    long AmountOffCents,
    int PercentOff,
    long MinOrderCents,
    CouponScope Scope,
    UserCouponStatus Status,
    DateTime? ValidToUtc);

/// <summary>
/// Coupon lifecycle: claiming from the coupon center, redemption codes that grant coins or
/// a coupon instance, applicability against a product and order total, and the used-again
/// restore that a cancelled or refunded order needs.
/// </summary>
public sealed partial class CouponService(DoggoDbContext db, CoinService coins, ILogger<CouponService> logger)
{
    /// <summary>Redeems a code once per user: coin codes credit the ledger, coupon codes
    /// issue a coupon instance. The count is bumped only on success.</summary>
    public async Task<RedeemResult> RedeemAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length == 0)
        {
            throw new CouponValidationException("A redeem code is required.");
        }

        var redeemCode = await db.RedeemCodes.FirstOrDefaultAsync(c => c.Code == normalized, cancellationToken)
                         ?? throw new CouponValidationException("This redeem code does not exist.");

        if (redeemCode.Status == RedeemCodeStatus.Disabled)
        {
            throw new CouponValidationException("This redeem code is disabled.");
        }
        if (redeemCode.ExpiresAtUtc is { } expires && expires <= DateTime.UtcNow)
        {
            redeemCode.Status = RedeemCodeStatus.Expired;
            throw new CouponValidationException("This redeem code has expired.");
        }
        if (redeemCode.RedeemedCount >= redeemCode.MaxRedemptions)
        {
            throw new CouponValidationException("This redeem code is fully redeemed.");
        }
        if (await db.UserRedeemRecords.AnyAsync(
                r => r.RedeemCodeId == redeemCode.Id && r.UserId == userId, cancellationToken))
        {
            throw new CouponValidationException("You have already redeemed this code.");
        }

        RedeemResult result;
        if (redeemCode.Type == RedeemCodeType.Coin)
        {
            if (redeemCode.CoinAmount <= 0)
            {
                throw new CouponValidationException("This redeem code carries no coins.");
            }

            var balance = await coins.GrantAsync(userId, redeemCode.CoinAmount, "redeem_code", redeemCode.Id.ToString(), null, cancellationToken);
            result = new RedeemResult(RedeemCodeType.Coin, redeemCode.CoinAmount, null, string.Empty, balance);
        }
        else
        {
            var coupon = redeemCode.CouponId is { } couponId
                ? await db.Coupons.FirstOrDefaultAsync(c => c.Id == couponId, cancellationToken)
                : null;
            if (coupon is null || !coupon.Enabled)
            {
                throw new CouponValidationException("The coupon behind this code is unavailable.");
            }

            await IssueCouponAsync(userId, coupon, cancellationToken);
            var balanceAfter = (await coins.GetBalanceAsync(userId, cancellationToken)).Balance;
            result = new RedeemResult(RedeemCodeType.Coupon, 0, coupon.Id, coupon.Title, balanceAfter);
        }

        redeemCode.RedeemedCount++;
        db.UserRedeemRecords.Add(new UserRedeemRecord
        {
            Id = Guid.CreateVersion7(),
            RedeemCodeId = redeemCode.Id,
            UserId = userId,
            RedeemedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        LogRedeemed(redeemCode.Code, userId, redeemCode.Type);
        return result;
    }

    /// <summary>Claims one instance of a coupon from the coupon center, honoring the
    /// per-user and total limits and the validity window.</summary>
    public async Task<CouponView> ClaimCouponAsync(Guid userId, Guid couponId, CancellationToken cancellationToken = default)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == couponId, cancellationToken)
                     ?? throw new CouponValidationException("This coupon does not exist.");

        var now = DateTime.UtcNow;
        if (!coupon.Enabled)
        {
            throw new CouponValidationException("This coupon is not available.");
        }
        if (coupon.ValidFromUtc is { } from && from > now)
        {
            throw new CouponValidationException("This coupon is not open for claiming yet.");
        }
        if (coupon.ValidToUtc is { } to && to <= now)
        {
            throw new CouponValidationException("This coupon has expired.");
        }
        if (coupon.TotalLimit is { } total)
        {
            var taken = await db.UserCoupons.CountAsync(uc => uc.CouponId == couponId, cancellationToken);
            if (taken >= total)
            {
                throw new CouponValidationException("This coupon is fully claimed.");
            }
        }

        await IssueCouponAsync(userId, coupon, cancellationToken);
        LogClaimed(coupon.Title, userId);
        return await ToViewAsync(userId, coupon.Id, cancellationToken);
    }

    /// <summary>Lists the current user's coupon instances, newest first.</summary>
    public async Task<IReadOnlyList<CouponView>> ListMyCouponsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var views = await db.UserCoupons.AsNoTracking()
            .Where(uc => uc.UserId == userId)
            .OrderByDescending(uc => uc.Id)
            .Join(db.Coupons.AsNoTracking(),
                uc => uc.CouponId,
                c => c.Id,
                (uc, c) => new CouponView(
                    uc.Id, c.Id, c.Title, c.Type, c.AmountOffCents, c.PercentOff,
                    c.MinOrderCents, c.Scope, uc.Status, uc.ValidToUtc))
            .ToListAsync(cancellationToken);
        return views;
    }

    /// <summary>
    /// Validates a user coupon against a product and order total and returns the deduction:
    /// the coupon must belong to the user, still be unused and in its window, cover the
    /// minimum order, and (for scoped coupons) list the product. Marks the coupon used
    /// against the given order.
    /// </summary>
    public async Task<long> ApplyCouponAsync(Guid userId, Guid userCouponId, Guid productId, long orderTotalCents, Guid orderId, CancellationToken cancellationToken = default)
    {
        var userCoupon = await db.UserCoupons.FirstOrDefaultAsync(
            uc => uc.Id == userCouponId && uc.UserId == userId, cancellationToken)
            ?? throw new CouponValidationException("This coupon does not belong to you.");

        if (userCoupon.Status != UserCouponStatus.Unused)
        {
            throw new CouponValidationException("This coupon is already used.");
        }

        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == userCoupon.CouponId, cancellationToken)
                     ?? throw new CouponValidationException("This coupon no longer exists.");
        if (!coupon.Enabled)
        {
            throw new CouponValidationException("This coupon is not available.");
        }

        var now = DateTime.UtcNow;
        if ((userCoupon.ValidToUtc is { } validTo && validTo <= now)
            || (coupon.ValidToUtc is { } couponTo && couponTo <= now))
        {
            throw new CouponValidationException("This coupon has expired.");
        }
        if (orderTotalCents < coupon.MinOrderCents)
        {
            throw new CouponValidationException($"This coupon needs an order of at least {coupon.MinOrderCents} cents.");
        }
        if (coupon.Scope == CouponScope.Products
            && !await db.CouponProducts.AnyAsync(cp => cp.CouponId == coupon.Id && cp.ProductId == productId, cancellationToken))
        {
            throw new CouponValidationException("This coupon does not apply to this product.");
        }

        var deduction = coupon.Type == CouponType.AmountOff
            ? coupon.AmountOffCents
            : orderTotalCents * coupon.PercentOff / 100;
        deduction = Math.Min(Math.Max(deduction, 0), orderTotalCents);

        userCoupon.Status = UserCouponStatus.Used;
        userCoupon.UsedOrderId = orderId;
        await db.SaveChangesAsync(cancellationToken);
        LogApplied(coupon.Title, userId, deduction);
        return deduction;
    }

    /// <summary>Gives a used coupon back when its order is cancelled or refunded.</summary>
    public async Task RestoreCouponAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var used = await db.UserCoupons
            .FirstOrDefaultAsync(uc => uc.UsedOrderId == orderId && uc.Status == UserCouponStatus.Used, cancellationToken);
        if (used is null)
        {
            return;
        }

        used.Status = UserCouponStatus.Unused;
        used.UsedOrderId = null;
        await db.SaveChangesAsync(cancellationToken);
        LogRestored(used.Id, orderId);
    }

    /// <summary>Issues one coupon instance and stamps the shared validity window onto it.</summary>
    private async Task IssueCouponAsync(Guid userId, Coupon coupon, CancellationToken cancellationToken)
    {
        var owned = await db.UserCoupons.CountAsync(uc => uc.UserId == userId && uc.CouponId == coupon.Id, cancellationToken);
        if (owned >= coupon.PerUserLimit)
        {
            throw new CouponValidationException("You have reached the claim limit for this coupon.");
        }

        db.UserCoupons.Add(new UserCoupon
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CouponId = coupon.Id,
            Status = UserCouponStatus.Unused,
            ValidToUtc = coupon.ValidToUtc
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CouponView> ToViewAsync(Guid userId, Guid couponId, CancellationToken cancellationToken)
    {
        var row = await db.UserCoupons.AsNoTracking()
            .Where(uc => uc.UserId == userId && uc.CouponId == couponId)
            .Join(db.Coupons.AsNoTracking(),
                uc => uc.CouponId,
                c => c.Id,
                (uc, c) => new CouponView(
                    uc.Id, c.Id, c.Title, c.Type, c.AmountOffCents, c.PercentOff,
                    c.MinOrderCents, c.Scope, uc.Status, uc.ValidToUtc))
            .FirstAsync(cancellationToken);
        return row;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "code {Code} redeemed by {UserId} ({Type})")]
    private partial void LogRedeemed(string code, Guid userId, RedeemCodeType type);

    [LoggerMessage(Level = LogLevel.Information, Message = "coupon {Title} claimed by {UserId}")]
    private partial void LogClaimed(string title, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "coupon {Title} applied by {UserId}, off {Deduction} cents")]
    private partial void LogApplied(string title, Guid userId, long deduction);

    [LoggerMessage(Level = LogLevel.Information, Message = "coupon {UserCouponId} restored for order {OrderId}")]
    private partial void LogRestored(Guid userCouponId, Guid orderId);
}
