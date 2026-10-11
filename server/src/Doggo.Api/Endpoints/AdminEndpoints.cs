using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
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
/// Back-office management surface behind the Admin policy: content, products, orders,
/// users, roll templates, and the dashboard counters. Admins are flagged on UserAccount
/// (IsAdmin); there is no self-service path to become one.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(new AuthorizeAttribute { Policy = "Admin" });

        // ---- dashboard ----
        admin.MapGet("/stats", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            return Results.Ok(new
            {
                users = await db.UserAccounts.CountAsync(cancellationToken),
                orders = await db.UserOrders.CountAsync(cancellationToken),
                paidOrders = await db.UserOrders.CountAsync(o => o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Cancelled, cancellationToken),
                revenueCents = await db.UserOrders.SumAsync(o => (long?)o.TotalCostCents, cancellationToken) ?? 0,
                newUsersToday = await db.UserAccounts.CountAsync(u => u.CreatedAtUtc >= now.Date, cancellationToken)
            });
        });

        // ---- content: articles ----
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

        // ---- content: banners ----
        admin.MapGet("/banners", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Banners.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync(cancellationToken)));

        admin.MapPost("/banners", async ([FromBody] BannerUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var banner = new Banner
            {
                Id = Guid.CreateVersion7(),
                Title = request.Title,
                ImageUrl = request.ImageUrl,
                LinkUrl = request.LinkUrl,
                Position = request.Position,
                SortOrder = request.SortOrder,
                Enabled = true
            };
            db.Banners.Add(banner);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/banners/{banner.Id}", banner);
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

        // ---- products: list + shelf status ----
        admin.MapGet("/products", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.Products.AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .ToListAsync(cancellationToken)));

        admin.MapPut("/products/{id:guid}/shelf", async (
            Guid id,
            [FromBody] ShelfRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is null)
            {
                return Results.Problem("Product was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            product.Status = request.OnShelf ? ProductStatus.OnShelf : ProductStatus.OffShelf;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { product.Id, product.Status });
        });

        // ---- orders: list + refund ----
        admin.MapGet("/orders", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.UserOrders.AsNoTracking().OrderByDescending(o => o.CreatedAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        admin.MapPost("/orders/{id:guid}/refund", async (
            Guid id,
            [FromBody] RefundAdminRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            var refunded = await orders.RefundOrderAsync(id, request.Reason, cancellationToken);
            return refunded ? Results.Ok(new { refunded }) : Results.Problem("Order cannot be refunded.", statusCode: StatusCodes.Status400BadRequest);
        });

        // ---- users: list + disable ----
        admin.MapGet("/users", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.UserAccounts.AsNoTracking().OrderByDescending(u => u.CreatedAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new AdminUserView(u.Id, u.Phone, u.Email, u.Nickname, u.InviteCode, u.Status, u.IsAdmin, u.CreatedAtUtc))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        admin.MapPut("/users/{id:guid}/status", async (
            Guid id,
            [FromBody] UserStatusRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (account is null)
            {
                return Results.Problem("User was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            account.Status = request.Disabled ? UserStatus.Disabled : UserStatus.Active;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { account.Id, account.Status });
        });

        // ---- roll templates: enable/disable ----
        admin.MapPut("/roll/templates/{id:guid}/enabled", async (
            Guid id,
            [FromBody] TemplateEnabledRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var template = await db.RollRoomTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (template is null)
            {
                return Results.Problem("Template was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            template.Enabled = request.Enabled;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { template.Id, template.Enabled });
        });

        return app;
    }
}

public sealed record ArticleUpsert(string Title, string? Summary, string ContentHtml, string Keywords, string Category);

public sealed record BannerUpsert(string Title, string ImageUrl, string LinkUrl, string Position, int SortOrder);

public sealed record ShelfRequest(bool OnShelf);

public sealed record RefundAdminRequest(string Reason);

public sealed record AdminUserView(
    Guid Id, string Phone, string Email, string Nickname, string InviteCode, UserStatus Status, bool IsAdmin, DateTime CreatedAtUtc);

public sealed record UserStatusRequest(bool Disabled);

public sealed record TemplateEnabledRequest(bool Enabled);
