using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public sealed record NoticeDto(Guid Id, string Title, string Content, DateTime? StartAtUtc, DateTime? EndAtUtc);

public sealed record BannerDto(Guid Id, string Title, string ImageUrl, string LinkUrl, string Position, int SortOrder);

public sealed record ArticleDto(
    Guid Id, string Title, string? BannerUrl, string? Summary, string ContentHtml, string Keywords,
    string Category, long ViewCount, DateTime CreatedAtUtc);

/// <summary>Operator-managed content surfaced to clients: notices, banners and articles.</summary>
public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notices", Notices);
        app.MapGet("/api/banners", Banners);
        app.MapGet("/api/articles/{id:guid}", Article);
        return app;
    }

    private static async Task<IResult> Notices(DoggoDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var notices = await db.Notices
            .AsNoTracking()
            .Where(n => n.Enabled
                && (!n.StartAtUtc.HasValue || n.StartAtUtc <= now)
                && (!n.EndAtUtc.HasValue || n.EndAtUtc >= now))
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(notices.Select(n => new NoticeDto(n.Id, n.Title, n.Content, n.StartAtUtc, n.EndAtUtc)).ToList());
    }

    private static async Task<IResult> Banners(DoggoDbContext db, [FromQuery] string? position, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var banners = db.Banners
            .AsNoTracking()
            .Where(b => b.Enabled)
            .Where(b => (b.StartAtUtc == null || b.StartAtUtc <= now)
                && (b.EndAtUtc == null || b.EndAtUtc > now))
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(position))
        {
            var requested = position.Trim();
            banners = banners.Where(b => b.Position == requested);
        }

        var list = await banners
            .OrderBy(b => b.SortOrder)
            .ThenByDescending(b => b.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(list.Select(b => new BannerDto(b.Id, b.Title, b.ImageUrl, b.LinkUrl, b.Position, b.SortOrder)).ToList());
    }

    private static async Task<IResult> Article(DoggoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (article is null || !article.Enabled)
        {
            return Results.NotFound(new { message = "Article not found." });
        }

        article.ViewCount++;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new ArticleDto(
            article.Id, article.Title, article.BannerUrl, article.Summary, article.ContentHtml,
            article.Keywords, article.Category, article.ViewCount, article.CreatedAtUtc));
    }
}
