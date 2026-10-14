using Doggo.Domain.Common;

namespace Doggo.Domain.News;

public enum GameKind
{
    Shared = 0,
    Cs2 = 1,
    Dota2 = 2
}

public enum NewsSourceType
{
    Rss = 0,
    JsonApi = 1,
    Html = 2
}

public class NewsSource : Entity
{
    public string Name { get; set; } = string.Empty;
    public NewsSourceType SourceType { get; set; }
    public GameKind Game { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ConfigJson { get; set; }
    public int FetchIntervalMinutes { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public DateTime? LastFetchedAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
}

public class NewsItem : Entity, ISoftDeletable
{
    public Guid SourceId { get; set; }
    public GameKind Game { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? ContentHtml { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>SHA-256 of the canonical URL; unique key used for dedup.</summary>
    public string UrlHash { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }
    public long ViewCount { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public class Article : Entity, ISoftDeletable
{
    public string Title { get; set; } = string.Empty;
    public string? BannerUrl { get; set; }
    public string? Summary { get; set; }
    public string ContentHtml { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public long ViewCount { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? DeletedAtUtc { get; set; }
}

public class Notice : Entity
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
}

public class Banner : Entity
{
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string LinkUrl { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Open slot: null means the banner is not bound to a delivery window.</summary>
    public DateTime? StartAtUtc { get; set; }
    public DateTime? EndAtUtc { get; set; }
}

public class Partner : Entity
{
    public string Name { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string LinkUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}
