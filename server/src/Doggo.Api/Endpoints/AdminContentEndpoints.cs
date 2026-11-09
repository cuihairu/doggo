using System.Globalization;
using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.System;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

/// <summary>
/// Back-office content surface: articles, banners, notices, promotions and the news
/// scraper sources/items, behind the shared Admin policy.
/// </summary>
public static class AdminContentEndpoints
{
    public static IEndpointRouteBuilder MapAdminContentEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/articles", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Articles.AsNoTracking()
                .OrderByDescending(a => a.CreatedAtUtc)
                .ToListAsync(cancellationToken)));

        admin.MapPost("/articles", async ([FromBody] ArticleUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var article = new Article
            {
                Id = Guid.CreateVersion7(),
                Title = request.Title,
                Summary = request.Summary,
                ContentHtml = request.ContentHtml,
                Keywords = request.Keywords,
                Category = request.Category,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Articles.Add(article);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/articles/{article.Id}", article);
        });

        admin.MapPut("/articles/{id:guid}", async (
            Guid id,
            [FromBody] ArticleUpsert request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (article is null)
            {
                return Results.Problem("Article was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            article.Title = request.Title;
            article.Summary = request.Summary;
            article.ContentHtml = request.ContentHtml;
            article.Keywords = request.Keywords;
            article.Category = request.Category;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(article);
        });

        admin.MapDelete("/articles/{id:guid}", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (article is null)
            {
                return Results.Problem("Article was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            db.Articles.Remove(article);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        admin.MapGet("/banners", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Banners.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync(cancellationToken)));

        admin.MapPost("/banners", async ([FromBody] BannerUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            if (await ValidateBannerWindowAsync(db, request, excludeId: null, cancellationToken) is { } error)
            {
                return error;
            }

            var banner = new Banner
            {
                Id = Guid.CreateVersion7(),
                Title = request.Title,
                ImageUrl = request.ImageUrl,
                LinkUrl = request.LinkUrl,
                Position = request.Position,
                SortOrder = request.SortOrder,
                Enabled = request.Enabled,
                StartAtUtc = request.StartAtUtc,
                EndAtUtc = request.EndAtUtc
            };
            db.Banners.Add(banner);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/banners/{banner.Id}", banner);
        });

        admin.MapPut("/banners/{id:guid}", async (
            Guid id,
            [FromBody] BannerUpsert request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var banner = await db.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (banner is null)
            {
                return Results.Problem("Banner was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            if (await ValidateBannerWindowAsync(db, request, excludeId: id, cancellationToken) is { } error)
            {
                return error;
            }

            banner.Title = request.Title;
            banner.ImageUrl = request.ImageUrl;
            banner.LinkUrl = request.LinkUrl;
            banner.Position = request.Position;
            banner.SortOrder = request.SortOrder;
            banner.Enabled = request.Enabled;
            banner.StartAtUtc = request.StartAtUtc;
            banner.EndAtUtc = request.EndAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(banner);
        });

        admin.MapDelete("/banners/{id:guid}", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var banner = await db.Banners.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (banner is null)
            {
                return Results.Problem("Banner was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            banner.Enabled = false;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        admin.MapGet("/notices", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Notices.AsNoTracking().OrderByDescending(n => n.StartAtUtc).ToListAsync(cancellationToken)));

        admin.MapPost("/notices", async ([FromBody] NoticeUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var notice = new Notice
            {
                Id = Guid.CreateVersion7(),
                Title = request.Title,
                Content = request.Content,
                Enabled = true,
                StartAtUtc = request.StartAtUtc,
                EndAtUtc = request.EndAtUtc
            };
            db.Notices.Add(notice);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/notices/{notice.Id}", notice);
        });

        admin.MapDelete("/notices/{id:guid}", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var notice = await db.Notices.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (notice is null)
            {
                return Results.Problem("Notice was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            db.Notices.Remove(notice);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        admin.MapGet("/promotions", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var promotions = await db.PromotionPlans.AsNoTracking().OrderByDescending(p => p.Enabled).ToListAsync(cancellationToken);
            var productIds = promotions.Select(p => p.ProductId).Distinct().ToList();
            var names = await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            return Results.Ok(promotions.Select(p => new
            {
                p.Id,
                p.ProductId,
                ProductName = names.GetValueOrDefault(p.ProductId, string.Empty),
                p.DiscountPriceCents,
                p.Stock,
                p.PerUserLimit,
                p.DailyReset,
                p.StartAtUtc,
                p.EndAtUtc,
                p.Enabled
            }));
        });

        admin.MapPut("/promotions/{id:guid}/enabled", async (
            Guid id,
            [FromBody] EnabledRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var promotion = await db.PromotionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (promotion is null)
            {
                return Results.Problem("Promotion was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            promotion.Enabled = request.Enabled;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { promotion.Id, promotion.Enabled });
        });
        admin.MapGet("/news-sources", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.NewsSources.AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync(cancellationToken)));

        admin.MapPut("/news-sources/{id:guid}/enabled", async (
            Guid id,
            [FromBody] EnabledRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var source = await db.NewsSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (source is null)
            {
                return Results.Problem("News source was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            source.Enabled = request.Enabled;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { source.Id, source.Enabled });
        });
        admin.MapGet("/news-items", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.NewsItems.AsNoTracking().OrderByDescending(n => n.PublishedAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });
        return admin;
    }

    /// <summary>
    /// Shared window guard for banner create/update: range sanity plus a same-position
    /// overlap check against every other enabled banner.
    /// </summary>
    private static async Task<IResult?> ValidateBannerWindowAsync(
        DoggoDbContext db,
        BannerUpsert request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (BannerRules.Validate(request.StartAtUtc, request.EndAtUtc) is { } rangeError)
        {
            return Results.Problem(rangeError, statusCode: StatusCodes.Status400BadRequest);
        }

        var candidates = await db.Banners.AsNoTracking()
            .Where(b => b.Enabled
                && b.Position == request.Position
                && (excludeId == null || b.Id != excludeId))
            .ToListAsync(cancellationToken);
        if (candidates.Any(c => BannerRules.Overlaps(c.StartAtUtc, c.EndAtUtc, request.StartAtUtc, request.EndAtUtc)))
        {
            return Results.Problem(
                "Another enabled banner for this position overlaps the delivery window.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return null;
    }
public sealed record ArticleUpsert(string Title, string? Summary, string ContentHtml, string Keywords, string Category);
public sealed record BannerUpsert(
    string Title,
    string ImageUrl,
    string LinkUrl,
    string Position,
    int SortOrder,
    DateTime? StartAtUtc = null,
    DateTime? EndAtUtc = null,
    bool Enabled = true);
public sealed record NoticeUpsert(string Title, string Content, DateTime? StartAtUtc, DateTime? EndAtUtc);
public static class BannerRules
{
    public static string? Validate(DateTime? startAtUtc, DateTime? endAtUtc)
    {
        if (startAtUtc is { } start && endAtUtc is { } end && end <= start)
        {
            return "Banner end time must be after start time.";
        }

        return null;
    }

    /// <summary>Half-open [start, end) windows; a null bound is unbounded.</summary>
    public static bool Overlaps(DateTime? aStart, DateTime? aEnd, DateTime? bStart, DateTime? bEnd)
    {
        if (aStart is { } aStartValue && bEnd is { } bEndValue && bEndValue <= aStartValue)
        {
            return false;
        }

        if (bStart is { } bStartValue && aEnd is { } aEndValue && aEndValue <= bStartValue)
        {
            return false;
        }

        return true;
    }
}
}
