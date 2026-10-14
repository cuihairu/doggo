using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Price;
using Doggo.Domain.Products;
using Doggo.Domain.System;
using Doggo.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Doggo.Infrastructure.Persistence.Configurations;

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.HasIndex(x => x.InviteCode).IsUnique();
        builder.HasIndex(x => x.WechatOpenId).IsUnique();
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => x.Email);
    }
}

public class UserRefreshTokenConfiguration : IEntityTypeConfiguration<UserRefreshToken>
{
    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
    }
}

public class UserSteamProfileConfiguration : IEntityTypeConfiguration<UserSteamProfile>
{
    public void Configure(EntityTypeBuilder<UserSteamProfile> builder) =>
        builder.HasIndex(x => x.SteamId).IsUnique();
}

public class UserCoinConfiguration : IEntityTypeConfiguration<UserCoin>
{
    public void Configure(EntityTypeBuilder<UserCoin> builder) => builder.HasIndex(x => x.UserId).IsUnique();
}

public class RedeemCodeConfiguration : IEntityTypeConfiguration<RedeemCode>
{
    public void Configure(EntityTypeBuilder<RedeemCode> builder) => builder.HasIndex(x => x.Code).IsUnique();
}

public class UserRedeemRecordConfiguration : IEntityTypeConfiguration<UserRedeemRecord>
{
    public void Configure(EntityTypeBuilder<UserRedeemRecord> builder) =>
        // A user redeems a code once even if the code allows many redemptions overall.
        builder.HasIndex(x => new { x.RedeemCodeId, x.UserId }).IsUnique();
}

public class SignRecordConfiguration : IEntityTypeConfiguration<SignRecord>
{
    public void Configure(EntityTypeBuilder<SignRecord> builder)
    {
        // One sign row per user per day; the cycle is derived, not unique (a week holds
        // several days).
        builder.HasIndex(x => new { x.UserId, x.SignedDate }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CycleId });

        // MySql.EntityFrameworkCore materializes DATE columns as DateTime and chokes on
        // DateOnly, so convert explicitly and keep the date column type.
        builder.Property(x => x.CycleId)
            .HasConversion(
                date => date.ToDateTime(TimeOnly.MinValue),
                dateTime => DateOnly.FromDateTime(dateTime))
            .HasColumnType("date");
        builder.Property(x => x.SignedDate)
            .HasConversion(
                date => date.ToDateTime(TimeOnly.MinValue),
                dateTime => DateOnly.FromDateTime(dateTime))
            .HasColumnType("date");
    }
}

public class CouponProductConfiguration : IEntityTypeConfiguration<CouponProduct>
{
    public void Configure(EntityTypeBuilder<CouponProduct> builder) =>
        builder.HasIndex(x => new { x.CouponId, x.ProductId }).IsUnique();
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder) => builder.HasIndex(x => x.ClassId);
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.HasIndex(x => x.CurrencyCode).IsUnique();
        builder.Property(x => x.Rate).HasPrecision(18, 8);
    }
}

public class UserOrderConfiguration : IEntityTypeConfiguration<UserOrder>
{
    public void Configure(EntityTypeBuilder<UserOrder> builder)
    {
        builder.HasIndex(x => x.OrderNo).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Status });
    }
}

public class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.OrderId);
        builder.Property(x => x.CallbackPayload).HasMaxLength(10_000);
    }
}

public class PaymentChannelConfiguration : IEntityTypeConfiguration<PaymentChannel>
{
    public void Configure(EntityTypeBuilder<PaymentChannel> builder)
    {
        builder.HasIndex(x => x.Provider).IsUnique();
        builder.Property(x => x.MerchantId).HasMaxLength(64);
        builder.Property(x => x.AppId).HasMaxLength(64);
        builder.Property(x => x.SecretRef).HasMaxLength(64);
        builder.Property(x => x.Note).HasMaxLength(200);
    }
}

public class GroupBuyingConfiguration : IEntityTypeConfiguration<GroupBuying>
{
    public void Configure(EntityTypeBuilder<GroupBuying> builder)
    {
        builder.HasIndex(x => new { x.ProductId, x.Lot }).IsUnique();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}

public class GroupBuyingSeatConfiguration : IEntityTypeConfiguration<GroupBuyingSeat>
{
    public void Configure(EntityTypeBuilder<GroupBuyingSeat> builder) =>
        builder.HasIndex(x => new { x.GroupBuyingId, x.SeatNo }).IsUnique();
}

public class UserPrizeConfiguration : IEntityTypeConfiguration<UserPrize>
{
    public void Configure(EntityTypeBuilder<UserPrize> builder)
    {
        builder.HasIndex(x => x.AssetId);
        builder.HasIndex(x => new { x.UserId, x.OrderId });
        // One prize per (order, asset) keeps replayed opening results from duplicating rows.
        builder.HasIndex(x => new { x.OrderId, x.AssetId }).IsUnique();
    }
}

public class RollRoomParticipantConfiguration : IEntityTypeConfiguration<RollRoomParticipant>
{
    public void Configure(EntityTypeBuilder<RollRoomParticipant> builder) =>
        builder.HasIndex(x => new { x.RoomId, x.UserId });
}

public class NewsSourceConfiguration : IEntityTypeConfiguration<NewsSource>
{
    public void Configure(EntityTypeBuilder<NewsSource> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();
        builder.Property(x => x.Url).HasMaxLength(2048);
        builder.Property(x => x.ConfigJson).HasColumnType("longtext");
    }
}

public class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        builder.HasIndex(x => x.UrlHash).IsUnique();
        builder.HasIndex(x => new { x.Game, x.PublishedAtUtc });
        builder.Property(x => x.ContentHtml).HasColumnType("longtext");
        builder.Property(x => x.Url).HasMaxLength(2048);
    }
}

public class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder) =>
        builder.Property(x => x.ContentHtml).HasColumnType("longtext");
}

public class MarketItemConfiguration : IEntityTypeConfiguration<MarketItem>
{
    public void Configure(EntityTypeBuilder<MarketItem> builder)
    {
        // Steam class ids are per-app, so uniqueness is (game, class id). Aggregates without a
        // class id are excluded from this index so several of them can coexist.
        builder.HasIndex(x => new { x.Game, x.ClassId })
            .IsUnique()
            .HasFilter("`ClassId` <> ''");
        builder.HasIndex(x => new { x.Game, x.MarketHashName }).IsUnique();
        builder.Property(x => x.MarketHashName).HasMaxLength(256);
        builder.Property(x => x.ClassId).HasMaxLength(64);
        builder.Property(x => x.IconUrl).HasMaxLength(2048);
    }
}

public class MarketPriceSnapshotConfiguration : IEntityTypeConfiguration<MarketPriceSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketPriceSnapshot> builder) =>
        builder.HasIndex(x => new { x.MarketItemId, x.CapturedAtUtc });
}

public class MarketPriceDailyConfiguration : IEntityTypeConfiguration<MarketPriceDaily>
{
    public void Configure(EntityTypeBuilder<MarketPriceDaily> builder)
    {
        builder.HasIndex(x => new { x.MarketItemId, x.Date }).IsUnique();

        // MySql.EntityFrameworkCore materializes DATE columns as DateTime and chokes on
        // DateOnly, so convert explicitly and keep the date column type.
        builder.Property(x => x.Date)
            .HasConversion(
                date => date.ToDateTime(TimeOnly.MinValue),
                dateTime => DateOnly.FromDateTime(dateTime))
            .HasColumnType("date");
    }
}

public class SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor>
{
    public void Configure(EntityTypeBuilder<SyncCursor> builder) => builder.HasIndex(x => x.SourceName).IsUnique();
}

public class AdminAccountConfiguration : IEntityTypeConfiguration<AdminAccount>
{
    public void Configure(EntityTypeBuilder<AdminAccount> builder) => builder.HasIndex(x => x.Username).IsUnique();
}

public class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder) => builder.HasIndex(x => x.Name).IsUnique();
}

public class AdminPermissionConfiguration : IEntityTypeConfiguration<AdminPermission>
{
    public void Configure(EntityTypeBuilder<AdminPermission> builder) => builder.HasIndex(x => x.Code).IsUnique();
}

public class SiteSettingConfiguration : IEntityTypeConfiguration<SiteSetting>
{
    public void Configure(EntityTypeBuilder<SiteSetting> builder) => builder.HasIndex(x => x.Key).IsUnique();
}

public class ShortUrlConfiguration : IEntityTypeConfiguration<ShortUrl>
{
    public void Configure(EntityTypeBuilder<ShortUrl> builder) => builder.HasIndex(x => x.Key).IsUnique();
}

public class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> builder) =>
        builder.Property(x => x.ConfigJson).HasMaxLength(4_000);
}

public class PriceCrawlItemMapConfiguration : IEntityTypeConfiguration<PriceCrawlItemMap>
{
    public void Configure(EntityTypeBuilder<PriceCrawlItemMap> builder)
    {
        builder.HasIndex(x => new { x.SiteKey, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.MapStatus, x.SiteKey });
        builder.HasIndex(x => x.MarketItemId);
    }
}

public class PriceCrawlRawConfiguration : IEntityTypeConfiguration<PriceCrawlRaw>
{
    public void Configure(EntityTypeBuilder<PriceCrawlRaw> builder)
    {
        builder.HasIndex(x => new { x.SiteKey, x.ExternalId, x.ObservedAtUtc });
        builder.HasIndex(x => x.FetchedAtUtc); // retention purge scans on this
        builder.Property(x => x.RawPriceText).HasMaxLength(512);
    }
}

public class PriceCrawlSeriesConfiguration : IEntityTypeConfiguration<PriceCrawlSeries>
{
    public void Configure(EntityTypeBuilder<PriceCrawlSeries> builder)
    {
        // Window dedup: one cleaned point per (map, window); the window key already encodes
        // the site's observation bucket through its map.
        builder.HasIndex(x => new { x.MapId, x.WindowKey }).IsUnique();
        builder.HasIndex(x => new { x.MarketItemId, x.ObservedAtUtc });
        builder.Property(x => x.WindowKey).HasMaxLength(16);
    }
}

public class PriceCrawlSiteStateConfiguration : IEntityTypeConfiguration<PriceCrawlSiteState>
{
    public void Configure(EntityTypeBuilder<PriceCrawlSiteState> builder) =>
        builder.HasIndex(x => x.SiteKey).IsUnique();
}

public class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> builder)
    {
        // Idempotent ingest: offline catch-up and client retries replay the same key.
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.EventName, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.SessionId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.VisitorKey, x.OccurredAtUtc });
        builder.Property(x => x.PropertiesJson).HasColumnType("longtext");
    }
}

public class FunnelDailyStatConfiguration : IEntityTypeConfiguration<FunnelDailyStat>
{
    public void Configure(EntityTypeBuilder<FunnelDailyStat> builder)
    {
        // One row per (day, client, channel); the rolling recompute upserts by this key.
        builder.HasIndex(x => new { x.Date, x.Client, x.Channel }).IsUnique();

        // MySql.EntityFrameworkCore materializes DATE columns as DateTime and chokes on
        // DateOnly, so convert explicitly and keep the date column type.
        builder.Property(x => x.Date)
            .HasConversion(
                date => date.ToDateTime(TimeOnly.MinValue),
                dateTime => DateOnly.FromDateTime(dateTime))
            .HasColumnType("date");
    }
}
