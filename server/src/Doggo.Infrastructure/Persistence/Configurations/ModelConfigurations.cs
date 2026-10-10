using Doggo.Domain.Cases;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
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
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => x.Email);
    }
}

public class UserCoinConfiguration : IEntityTypeConfiguration<UserCoin>
{
    public void Configure(EntityTypeBuilder<UserCoin> builder) => builder.HasIndex(x => x.UserId).IsUnique();
}

public class RedeemCodeConfiguration : IEntityTypeConfiguration<RedeemCode>
{
    public void Configure(EntityTypeBuilder<RedeemCode> builder) => builder.HasIndex(x => x.Code).IsUnique();
}

public class SignRecordConfiguration : IEntityTypeConfiguration<SignRecord>
{
    public void Configure(EntityTypeBuilder<SignRecord> builder) =>
        builder.HasIndex(x => new { x.UserId, x.CycleId }).IsUnique();
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
    }
}

public class RollRoomParticipantConfiguration : IEntityTypeConfiguration<RollRoomParticipant>
{
    public void Configure(EntityTypeBuilder<RollRoomParticipant> builder) =>
        builder.HasIndex(x => new { x.RoomId, x.UserId });
}

public class NewsSourceConfiguration : IEntityTypeConfiguration<NewsSource>
{
    public void Configure(EntityTypeBuilder<NewsSource> builder) => builder.HasIndex(x => x.Name).IsUnique();
}

public class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        builder.HasIndex(x => x.UrlHash).IsUnique();
        builder.HasIndex(x => new { x.Game, x.PublishedAtUtc });
        builder.Property(x => x.ContentHtml).HasColumnType("longtext");
    }
}

public class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder) =>
        builder.Property(x => x.ContentHtml).HasColumnType("longtext");
}

public class MarketItemConfiguration : IEntityTypeConfiguration<MarketItem>
{
    public void Configure(EntityTypeBuilder<MarketItem> builder) => builder.HasIndex(x => x.ClassId).IsUnique();
}

public class MarketPriceSnapshotConfiguration : IEntityTypeConfiguration<MarketPriceSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketPriceSnapshot> builder) =>
        builder.HasIndex(x => new { x.MarketItemId, x.CapturedAtUtc });
}

public class MarketPriceDailyConfiguration : IEntityTypeConfiguration<MarketPriceDaily>
{
    public void Configure(EntityTypeBuilder<MarketPriceDaily> builder) =>
        builder.HasIndex(x => new { x.MarketItemId, x.Date }).IsUnique();
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
