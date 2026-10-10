using Doggo.Domain.Common;
using Doggo.Domain.News;

namespace Doggo.Domain.Market;

public enum MarketItemType
{
    Case = 0,
    Key = 1,
    Skin = 2,
    Other = 3
}

public class MarketItem : Entity
{
    public string ClassId { get; set; } = string.Empty;
    public string MarketHashName { get; set; } = string.Empty;
    public MarketItemType ItemType { get; set; }
    public GameKind Game { get; set; }
    public string IconUrl { get; set; } = string.Empty;
}

/// <summary>Raw price point synced from vapor.</summary>
public class MarketPriceSnapshot : Entity
{
    public Guid MarketItemId { get; set; }
    public long PriceCents { get; set; }
    public string Currency { get; set; } = "CNY";
    public DateTime CapturedAtUtc { get; set; }
}

/// <summary>Daily OHLC aggregate for trend charts.</summary>
public class MarketPriceDaily : Entity
{
    public Guid MarketItemId { get; set; }
    public DateOnly Date { get; set; }
    public long OpenCents { get; set; }
    public long HighCents { get; set; }
    public long LowCents { get; set; }
    public long CloseCents { get; set; }
}

/// <summary>Per-source incremental cursor for sync jobs.</summary>
public class SyncCursor : Entity
{
    public string SourceName { get; set; } = string.Empty;
    public string Cursor { get; set; } = string.Empty;
}
