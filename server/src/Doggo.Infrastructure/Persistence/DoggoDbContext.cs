using System.Linq.Expressions;
using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
using Doggo.Domain.Common;
using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Price;
using Doggo.Domain.Products;
using Doggo.Domain.System;
using Doggo.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Infrastructure.Persistence;

/// <summary>
/// Application database context. Entities are registered here as domains land (see docs/design/data-model.md).
/// </summary>
public class DoggoDbContext(DbContextOptions<DoggoDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<UserSteamProfile> UserSteamProfiles => Set<UserSteamProfile>();
    public DbSet<UserTradeUrl> UserTradeUrls => Set<UserTradeUrl>();
    public DbSet<UserSteamBackupCode> UserSteamBackupCodes => Set<UserSteamBackupCode>();
    public DbSet<UserCoin> UserCoins => Set<UserCoin>();
    public DbSet<UserCoinBatch> UserCoinBatches => Set<UserCoinBatch>();
    public DbSet<UserCoinLog> UserCoinLogs => Set<UserCoinLog>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<UserCoupon> UserCoupons => Set<UserCoupon>();
    public DbSet<CouponProduct> CouponProducts => Set<CouponProduct>();
    public DbSet<RedeemCode> RedeemCodes => Set<RedeemCode>();
    public DbSet<UserRedeemRecord> UserRedeemRecords => Set<UserRedeemRecord>();
    public DbSet<UserLoginLog> UserLoginLogs => Set<UserLoginLog>();
    public DbSet<UserVisitLog> UserVisitLogs => Set<UserVisitLog>();
    public DbSet<InviteRewardLog> InviteRewardLogs => Set<InviteRewardLog>();
    public DbSet<SignRecord> SignRecords => Set<SignRecord>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<CasePlan> CasePlans => Set<CasePlan>();
    public DbSet<CasePlanOption> CasePlanOptions => Set<CasePlanOption>();
    public DbSet<PromotionPlan> PromotionPlans => Set<PromotionPlan>();
    public DbSet<QuantityOption> QuantityOptions => Set<QuantityOption>();
    public DbSet<CaseContent> CaseContents => Set<CaseContent>();
    public DbSet<ProductRecommend> ProductRecommends => Set<ProductRecommend>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<UserOrder> UserOrders => Set<UserOrder>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<RefundRecord> RefundRecords => Set<RefundRecord>();

    public DbSet<GroupBuying> GroupBuyings => Set<GroupBuying>();
    public DbSet<GroupBuyingSeat> GroupBuyingSeats => Set<GroupBuyingSeat>();
    public DbSet<UserPrize> UserPrizes => Set<UserPrize>();
    public DbSet<UserPrizeLog> UserPrizeLogs => Set<UserPrizeLog>();
    public DbSet<RollRoomTemplate> RollRoomTemplates => Set<RollRoomTemplate>();
    public DbSet<RollRoom> RollRooms => Set<RollRoom>();
    public DbSet<RollRoomPrize> RollRoomPrizes => Set<RollRoomPrize>();
    public DbSet<RollRoomParticipant> RollRoomParticipants => Set<RollRoomParticipant>();

    public DbSet<NewsSource> NewsSources => Set<NewsSource>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Partner> Partners => Set<Partner>();

    public DbSet<MarketItem> MarketItems => Set<MarketItem>();
    public DbSet<MarketPriceSnapshot> MarketPriceSnapshots => Set<MarketPriceSnapshot>();
    public DbSet<MarketPriceDaily> MarketPriceDailies => Set<MarketPriceDaily>();
    public DbSet<SyncCursor> SyncCursors => Set<SyncCursor>();

    public DbSet<PriceCrawlItemMap> PriceCrawlItemMaps => Set<PriceCrawlItemMap>();
    public DbSet<PriceCrawlRaw> PriceCrawlRaws => Set<PriceCrawlRaw>();
    public DbSet<PriceCrawlSeries> PriceCrawlSeriesPoints => Set<PriceCrawlSeries>();
    public DbSet<PriceCrawlSiteState> PriceCrawlSiteStates => Set<PriceCrawlSiteState>();

    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<FunnelDailyStat> FunnelDailyStats => Set<FunnelDailyStat>();

    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();
    public DbSet<AdminRole> AdminRoles => Set<AdminRole>();
    public DbSet<AdminPermission> AdminPermissions => Set<AdminPermission>();
    public DbSet<AdminUserRole> AdminUserRoles => Set<AdminUserRole>();
    public DbSet<AdminRolePermission> AdminRolePermissions => Set<AdminRolePermission>();
    public DbSet<AdminIpWhitelist> AdminIpWhitelists => Set<AdminIpWhitelist>();
    public DbSet<AdminLoginLog> AdminLoginLogs => Set<AdminLoginLog>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<SupportQrcode> SupportQrcodes => Set<SupportQrcode>();
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationEventSubscription> NotificationEventSubscriptions => Set<NotificationEventSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DoggoDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Ids are generated application-side (GUID v7); keep providers from generating their own.
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            // Default string columns to varchar(255); long-text columns override via configuration.
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(string) && property.GetMaxLength() is null && !property.IsKey())
                {
                    property.SetMaxLength(255);
                }
            }

            // Global soft-delete filter for every entity implementing ISoftDeletable.
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var body = Expression.Equal(
                    Expression.Property(parameter, nameof(ISoftDeletable.DeletedAtUtc)),
                    Expression.Constant(null, typeof(DateTime?)));
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
            }
        }
    }

    public override int SaveChanges()
    {
        ApplyAuditAndIds();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndIds();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditAndIds()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                    {
                        entry.Entity.Id = Guid.CreateVersion7();
                    }

                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }
}
