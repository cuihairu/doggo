using Doggo.Domain.Products;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public sealed record ProductQuery(int Page = 1, int PageSize = 20, ProductType? Type = null, string? Q = null);

public sealed record ProductListItem(
    Guid Id,
    ProductType Type,
    string Name,
    string ClassId,
    string IconUrl,
    ProductStatus Status,
    long FinalPriceCents,
    long RegularPriceCents,
    int SortOrder);

public sealed record CasePlanOptionDto(Guid Id, int UpliftBps, int Quantity, int MinQuantity, long PriceCents, bool Enabled);

public sealed record CasePlanDto(Guid Id, string Title, int MinQuantity, int MaxQuantity, bool CanUseCoupon, bool CanUseCoin, int CashbackBps, bool Enabled, IReadOnlyList<CasePlanOptionDto> Options);

public sealed record CaseContentDto(string Title, CaseQuality Quality, string Color, string ImageUrl, long ReferencePriceCents, int SortOrder);

public sealed record QuantityOptionDto(int Quantity, long PriceCents);

public sealed record PromotionDto(Guid Id, Guid ProductId, string ProductName, string ProductIconUrl, long DiscountPriceCents, long OriginalPriceCents, int Stock, int PerUserLimit, bool DailyReset, DateTime? EndAtUtc);

public sealed record RecommendDto(Guid Id, Guid ProductId, string ProductName, string ProductIconUrl, string Title, string Description, string Type, int SortOrder);

public sealed record SkuDto(
    Guid? PlanId,
    Guid? OptionId,
    string ServiceMode,
    int Quantity,
    int MinQuantity,
    long UnitPriceCents,
    long TotalPriceCents,
    int CashbackBps,
    bool CanUseCoupon,
    bool CanUseCoin,
    string PriceSource);

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", List);
        app.MapGet("/api/products/{id:guid}", Detail);
        app.MapGet("/api/products/{id:guid}/skus", GetSkus);
        app.MapGet("/api/promotions", ListPromotions);
        app.MapGet("/api/recommends", ListRecommends);
        return app;
    }

    /// <summary>On-shelf crates and keys; soft-deleted rows are removed by the global filter.</summary>
    private static async Task<IResult> List(DoggoDbContext db, [AsParameters] ProductQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(query.Page, 1, 100_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var products = db.Products.AsNoTracking().Where(p => p.Status != ProductStatus.OffShelf);
        if (query.Type.HasValue)
        {
            products = products.Where(p => p.Type == query.Type.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var q = query.Q.Trim().ToLowerInvariant();
            products = products.Where(p => p.Name.ToLower().Contains(q));
        }

        var total = await products.CountAsync(cancellationToken);
        var items = await products
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductListItem(
                p.Id, p.Type, p.Name, p.ClassId, p.IconUrl, p.Status,
                p.FinalPriceCents, p.RegularPriceCents, p.SortOrder))
            .ToListAsync(cancellationToken);

        return Results.Ok(new { total, page, pageSize, items });
    }

    private static async Task<IResult> Detail(DoggoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null || product.Status == ProductStatus.OffShelf)
        {
            return Results.NotFound(new { message = "Product not found." });
        }

        var plans = await db.CasePlans.AsNoTracking()
            .Where(plan => plan.ProductId == id && plan.Enabled)
            .OrderBy(plan => plan.MinQuantity)
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(plan => plan.Id).ToList();
        var options = await db.CasePlanOptions.AsNoTracking()
            .Where(option => planIds.Contains(option.PlanId) && option.Enabled)
            .OrderBy(option => option.Quantity)
            .ToListAsync(cancellationToken);

        var contents = await db.CaseContents.AsNoTracking()
            .Where(content => content.ProductId == id)
            .OrderByDescending(content => content.Quality)
            .ThenBy(content => content.SortOrder)
            .ToListAsync(cancellationToken);

        var quantityTiers = await db.QuantityOptions.AsNoTracking()
            .Where(tier => tier.ProductId == id)
            .OrderBy(tier => tier.Quantity)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var promotion = await db.PromotionPlans.AsNoTracking()
            .FirstOrDefaultAsync(promotion => promotion.ProductId == id
                                              && promotion.Enabled
                                              && promotion.Stock > 0
                                              && (promotion.StartAtUtc == null || promotion.StartAtUtc <= now)
                                              && (promotion.EndAtUtc == null || promotion.EndAtUtc >= now),
                cancellationToken);

        var planDtos = plans
            .Select(plan => new CasePlanDto(
                plan.Id, plan.Title, plan.MinQuantity, plan.MaxQuantity,
                plan.CanUseCoupon, plan.CanUseCoin, plan.CashbackBps, plan.Enabled,
                options.Where(option => option.PlanId == plan.Id)
                    .Select(option => new CasePlanOptionDto(
                        option.Id, option.UpliftBps, option.Quantity, option.MinQuantity, option.PriceCents, option.Enabled))
                    .ToList()))
            .ToList();

        return Results.Ok(new
        {
            id = product.Id,
            type = product.Type,
            name = product.Name,
            classId = product.ClassId,
            iconUrl = product.IconUrl,
            status = product.Status,
            finalPriceCents = PriceCalculator.EffectiveUnitPriceCents(product),
            regularPriceCents = product.RegularPriceCents,
            buyNotice = product.BuyNotice,
            plans = planDtos,
            contents = contents.Select(content => new CaseContentDto(
                content.Title, content.Quality, content.Color, content.ImageUrl,
                content.ReferencePriceCents, content.SortOrder)),
            quantityTiers = quantityTiers.Select(tier => new QuantityOptionDto(tier.Quantity, tier.PriceCents)),
            promotion = promotion is null
                ? null
                : new PromotionDto(promotion.Id, promotion.ProductId, product.Name, product.IconUrl,
                    promotion.DiscountPriceCents, PriceCalculator.EffectiveUnitPriceCents(product),
                    promotion.Stock, promotion.PerUserLimit, promotion.DailyReset, promotion.EndAtUtc)
        });
    }

    /// <summary>Purchasable SKUs: plain single buys plus every enabled plan option tier.</summary>
    private static async Task<IResult> GetSkus(DoggoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null || product.Status == ProductStatus.OffShelf)
        {
            return Results.NotFound(new { message = "Product not found." });
        }

        var skus = new List<SkuDto>();

        if (product.Type == ProductType.Key || product.Type == ProductType.Crate)
        {
            var tiers = await db.QuantityOptions.AsNoTracking()
                .Where(tier => tier.ProductId == id)
                .ToListAsync(cancellationToken);
            var breakdown = PriceCalculator.Calculate(product, 1, tiers);
            skus.Add(new SkuDto(null, null, ServiceMode(product.Status), 1, 1,
                breakdown.UnitPriceCents, breakdown.TotalCents, 0,
                CanUseCoupon: true, CanUseCoin: true, breakdown.PriceSource));
        }

        var plans = await db.CasePlans.AsNoTracking()
            .Where(plan => plan.ProductId == id && plan.Enabled)
            .ToListAsync(cancellationToken);
        var planIds = plans.Select(plan => plan.Id).ToList();
        var options = await db.CasePlanOptions.AsNoTracking()
            .Where(option => planIds.Contains(option.PlanId) && option.Enabled)
            .ToListAsync(cancellationToken);

        foreach (var option in options.OrderBy(option => option.Quantity))
        {
            var plan = plans.First(plan => plan.Id == option.PlanId);
            var quantity = Math.Max(option.Quantity, 1);
            skus.Add(new SkuDto(
                plan.Id, option.Id, ServiceMode(product.Status),
                quantity, Math.Max(option.MinQuantity, 1),
                option.PriceCents, option.PriceCents * quantity,
                plan.CashbackBps, plan.CanUseCoupon, plan.CanUseCoin, PriceSource.PlanOption));
        }

        return Results.Ok(new { items = skus });
    }

    private static string ServiceMode(ProductStatus status) => status switch
    {
        ProductStatus.SelfOpenOnly => "self_open",
        ProductStatus.GroupOpenOnly => "group_open",
        _ => "both"
    };

    private static async Task<IResult> ListPromotions(DoggoDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var promotions = await db.PromotionPlans.AsNoTracking()
            .Where(promotion => promotion.Enabled
                                && promotion.Stock > 0
                                && (promotion.StartAtUtc == null || promotion.StartAtUtc <= now)
                                && (promotion.EndAtUtc == null || promotion.EndAtUtc >= now))
            .OrderBy(promotion => promotion.DiscountPriceCents)
            .ThenBy(promotion => promotion.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var productIds = promotions.Select(promotion => promotion.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var items = promotions
            .Where(promotion => products.ContainsKey(promotion.ProductId))
            .Select(promotion =>
            {
                var product = products[promotion.ProductId];
                return new PromotionDto(promotion.Id, promotion.ProductId, product.Name, product.IconUrl,
                    promotion.DiscountPriceCents, PriceCalculator.EffectiveUnitPriceCents(product),
                    promotion.Stock, promotion.PerUserLimit, promotion.DailyReset, promotion.EndAtUtc);
            })
            .ToList();

        return Results.Ok(new { items });
    }

    private static async Task<IResult> ListRecommends(DoggoDbContext db, CancellationToken cancellationToken)
    {
        var recommends = await db.ProductRecommends.AsNoTracking()
            .Where(recommend => recommend.Enabled)
            .OrderBy(recommend => recommend.SortOrder)
            .ToListAsync(cancellationToken);

        var productIds = recommends.Select(recommend => recommend.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var items = recommends
            .Where(recommend => products.ContainsKey(recommend.ProductId))
            .Select(recommend =>
            {
                var product = products[recommend.ProductId];
                return new RecommendDto(recommend.Id, recommend.ProductId, product.Name, product.IconUrl,
                    recommend.Title, recommend.Description, recommend.Type, recommend.SortOrder);
            })
            .ToList();

        return Results.Ok(new { items });
    }
}
