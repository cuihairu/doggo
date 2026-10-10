using Doggo.Domain.Common;

namespace Doggo.Domain.Users;

public enum UserStatus
{
    PendingActivation = 0,
    Active = 1,
    Disabled = 2
}

public enum CoinLogType
{
    Grant = 0,
    Spend = 1,
    Refund = 2,
    Expire = 3
}

public enum CouponType
{
    AmountOff = 0,
    PercentOff = 1
}

public enum CouponScope
{
    All = 0,
    Products = 1
}

public enum RedeemCodeType
{
    Coin = 0,
    Coupon = 1
}

public enum RedeemCodeStatus
{
    Active = 0,
    Disabled = 1,
    Expired = 2
}

public enum UserCouponStatus
{
    Unused = 0,
    Used = 1,
    Expired = 2
}

public enum TradeUrlStatus
{
    Unchecked = 0,
    Valid = 1,
    Invalid = 2
}

public class UserAccount : Entity, ISoftDeletable
{
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string InviteCode { get; set; } = string.Empty;
    public Guid? InvitedByUserId { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public bool RealNameVerified { get; set; }
    public DateTime? RealNameVerifiedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public class UserSteamProfile : Entity
{
    public Guid UserId { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
}

public class UserTradeUrl : Entity
{
    public Guid UserId { get; set; }
    public string SteamId { get; set; } = string.Empty;
    public string Partner { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public bool IsPreferred { get; set; }
    public TradeUrlStatus Status { get; set; } = TradeUrlStatus.Unchecked;
    public string? CheckError { get; set; }
    public DateTime? CheckedAtUtc { get; set; }
}

public class UserSteamBackupCode : Entity
{
    public Guid UserId { get; set; }
    public string Code { get; set; } = string.Empty;
}

/// <summary>Per-user coin balance (unit: 1 point).</summary>
public class UserCoin : Entity
{
    public Guid UserId { get; set; }
    public long Balance { get; set; }
}

/// <summary>Grant batch: coins expire per batch and are spent FIFO by expiry.</summary>
public class UserCoinBatch : Entity
{
    public Guid UserId { get; set; }
    public long OriginalAmount { get; set; }
    public long RemainingAmount { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? SourceRef { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

public class UserCoinLog : Entity
{
    public Guid UserId { get; set; }
    public CoinLogType Type { get; set; }
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
    public string? RefType { get; set; }
    public string? RefId { get; set; }
    public string? Note { get; set; }
}

public class Coupon : Entity, ISoftDeletable
{
    public string Title { get; set; } = string.Empty;
    public CouponType Type { get; set; }
    public long AmountOffCents { get; set; }
    public int PercentOff { get; set; }
    public long MinOrderCents { get; set; }
    public CouponScope Scope { get; set; } = CouponScope.All;
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int? TotalLimit { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? DeletedAtUtc { get; set; }
}

public class UserCoupon : Entity
{
    public Guid UserId { get; set; }
    public Guid CouponId { get; set; }
    public UserCouponStatus Status { get; set; } = UserCouponStatus.Unused;
    public Guid? UsedOrderId { get; set; }
    public DateTime? ValidToUtc { get; set; }
}

public class RedeemCode : Entity
{
    public RedeemCodeType Type { get; set; }
    public string Code { get; set; } = string.Empty;
    public string BatchNo { get; set; } = string.Empty;
    public Guid? CouponId { get; set; }
    public long CoinAmount { get; set; }
    public int MaxRedemptions { get; set; } = 1;
    public int RedeemedCount { get; set; }
    public RedeemCodeStatus Status { get; set; } = RedeemCodeStatus.Active;
    public DateTime? ExpiresAtUtc { get; set; }
}

public class UserRedeemRecord : Entity
{
    public Guid RedeemCodeId { get; set; }
    public Guid UserId { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
}

public class UserLoginLog : Entity
{
    public Guid? UserId { get; set; }
    public string Account { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public class UserVisitLog : Entity
{
    public DateOnly Date { get; set; }
    public Guid? UserId { get; set; }
    public string VisitorKey { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class InviteRewardLog : Entity
{
    public Guid InviterUserId { get; set; }
    public Guid InviteeUserId { get; set; }
    public long CoinReward { get; set; }
    public string Note { get; set; } = string.Empty;
}

public class SignRecord : Entity
{
    public Guid UserId { get; set; }
    public DateOnly CycleId { get; set; }
    public DateOnly SignedDate { get; set; }
    public long CoinReward { get; set; }

    /// <summary>Order that qualified for a make-up sign, if applicable.</summary>
    public string? MakeupOrderNo { get; set; }
}
