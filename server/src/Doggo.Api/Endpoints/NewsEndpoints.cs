using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public sealed record NewsQuery(int Page = 1, int PageSize = 20, GameKind? Game = null, string? Category = null, string? Tag = null);

public sealed record NewsListItem(
    Guid Id,
    string Title,
    GameKind Game,
    string Category,
    string Tags,
    string? Summary,
    string? CoverUrl,
    string Url,
    long ViewCount,
    DateTime PublishedAtUtc,
    DateTime CreatedAtUtc,
    string? SourceName);

public sealed record NewsDetail(NewsListItem Meta, string? Author, string? ContentHtml);

public static class NewsEndpoints
{
    public static IEndpointRouteBuilder MapNewsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/news", List);
        app.MapGet("/api/news/{id:guid}", Detail);
        return app;
    }

    private static async Task<IResult> List(DoggoDbContext db, [AsParameters] NewsQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(query.Page, 1, 100_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var items = db.NewsItems.AsNoTracking().AsQueryable();
        if (query.Game.HasValue)
        {
            items = items.Where(i => i.Game == query.Game.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            items = items.Where(i => i.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim();
            items = items.Where(i => i.Tags.Contains(tag));
        }

        var total = await items.CountAsync(cancellationToken);
        var pageItems = await items
            .OrderByDescending(i => i.PublishedAtUtc)
            .ThenByDescending(i => i.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var sourceIds = pageItems.Select(i => i.SourceId).Distinct().ToHashSet();
        var names = await db.NewsSources
            .AsNoTracking()
            .Where(s => sourceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var list = pageItems
            .Select(i => new NewsListItem(
                i.Id, i.Title, i.Game, i.Category, i.Tags, i.Summary, i.CoverUrl, i.Url,
                i.ViewCount, i.PublishedAtUtc, i.CreatedAtUtc, names.GetValueOrDefault(i.SourceId)))
            .ToList();

        return Results.Ok(new { total, page, pageSize, items = list });
    }

    private static async Task<IResult> Detail(DoggoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var item = await db.NewsItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound(new { message = "News item not found." });
        }

        var sourceName = await db.NewsSources
            .AsNoTracking()
            .Where(s => s.Id == item.SourceId)
            .Select(s => (string?)s.Name)
            .FirstOrDefaultAsync(cancellationToken);

        item.ViewCount++;
        await db.SaveChangesAsync(cancellationToken);

        var meta = new NewsListItem(
            item.Id, item.Title, item.Game, item.Category, item.Tags, item.Summary, item.CoverUrl,
            item.Url, item.ViewCount, item.PublishedAtUtc, item.CreatedAtUtc, sourceName);

        return Results.Ok(new NewsDetail(meta, item.Author, item.ContentHtml));
    }
}
