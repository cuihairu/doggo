using Doggo.Domain.Common;
using Doggo.Domain.Orders;
using Doggo.Domain.Users;

namespace Doggo.Domain.Cases;

public enum GroupBuyingStatus
{
    OnSale = 0,
    SoldOut = 1,
    Closed = 2
}

public enum SeatStatus
{
    OnSale = 0,
    Locked = 1,
    Paid = 2,
    Opening = 3,
    Opened = 4,
    FetchingStock = 5,
    StockReady = 6,
    Trading = 7,
    Completed = 8
}

public enum PrizeSource
{
    SelfOpen = 0,
    GroupOpen = 1,
    CloudOpen = 2,
    Roll = 3
}

public enum RollRuleType
{
    LoginEntry = 1,
    PerPurchase = 2,
    FreeBundle = 3,
    FixedInviteCode = 4,
    AllBlueFree = 5,
    RedDropFree = 6,
    GoldDropFree = 7,
    RandomInviteCode = 8
}

public enum RollRoomStatus
{
    Creating = 0,
    Running = 1,
    Drawing = 2,
    Drawn = 3,
    Claiming = 4,
    Finished = 5
}

public enum RollPrizeType
{
    InventoryItem = 0,
    CoinGrant = 1
}

/// <summary>
/// One batch = a series of cases loaded on a single Steam account.
/// Version is a manual concurrency token incremented by the seat-claiming flow.
/// </summary>
public class GroupBuying : Entity
{
    public Guid ProductId { get; set; }
    public string Lot { get; set; } = string.Empty;
    public int SeatCount { get; set; }
    public int SoldCount { get; set; }
    public GroupBuyingStatus Status { get; set; } = GroupBuyingStatus.OnSale;
    public long Version { get; set; }
    public DateTime? PublicityAtUtc { get; set; }
}

public class GroupBuyingSeat : Entity
{
    public Guid GroupBuyingId { get; set; }
    public int SeatNo { get; set; }
    public string? CrateAssetId { get; set; }
    public string? KeyAssetId { get; set; }
    public string? PrizeAssetId { get; set; }
    public long? HistoryId { get; set; }
    public SeatStatus Status { get; set; } = SeatStatus.OnSale;
    public Guid? OrderId { get; set; }
}

public class UserPrize : Entity
{
    public Guid UserId { get; set; }
    public Guid OrderId { get; set; }
    public PrizeSource Source { get; set; }

    public string AssetId { get; set; } = string.Empty;
    public string ClassId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string MarketHashName { get; set; } = string.Empty;
    public Products.CaseQuality Quality { get; set; }
    public string Exterior { get; set; } = string.Empty;
    public double? FloatValue { get; set; }
    public string Rarity { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string? InspectLink { get; set; }
    public long PriceCents { get; set; }
    public DateTime? TradableAtUtc { get; set; }
}

public class UserPrizeLog : Entity
{
    public Guid PrizeId { get; set; }
    public Guid UserId { get; set; }
    public Guid? OrderId { get; set; }
    public PrizeSource Source { get; set; }
    public string? RawJson { get; set; }
}

public class RollRoomTemplate : Entity
{
    public string Title { get; set; } = string.Empty;
    public RollRuleType RuleType { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public int EntryDeadlineMinutes { get; set; } = 5;
    public long MaxPrizeValueCents { get; set; }
    public bool Enabled { get; set; } = true;
}

public class RollRoom : Entity
{
    public Guid? TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public RollRuleType RuleType { get; set; }
    public RollRoomStatus Status { get; set; } = RollRoomStatus.Creating;
    public string? InviteCode { get; set; }
    public string? CoverUrl { get; set; }
    public long MaxPrizeValueCents { get; set; }
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
    public DateTime? EntryDeadlineUtc { get; set; }
}

public class RollRoomPrize : Entity
{
    public Guid RoomId { get; set; }
    public RollPrizeType Type { get; set; }
    public Guid? UserPrizeId { get; set; }
    public long CoinAmount { get; set; }
    public int Weight { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}

public class RollRoomParticipant : Entity
{
    public Guid RoomId { get; set; }
    public Guid UserId { get; set; }
    public string? EntryRef { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public Guid? WonPrizeId { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
}
