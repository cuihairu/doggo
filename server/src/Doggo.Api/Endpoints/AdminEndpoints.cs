using System.Globalization;
using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
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

        // ---- news: scraper sources and fetched items ----
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

        // ---- notices ----
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

        // ---- promotions: list with product name + enable toggle ----
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

        // ---- exchange rates: view + manual correction ----
        admin.MapGet("/exchange-rates", async (DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.ExchangeRates.AsNoTracking().OrderBy(r => r.CurrencyCode).ToListAsync(cancellationToken)));

        admin.MapPut("/exchange-rates/{id:guid}", async (
            Guid id,
            [FromBody] RateUpdateRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var rate = await db.ExchangeRates.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (rate is null)
            {
                return Results.Problem("Exchange rate was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            rate.Rate = request.Rate;
            rate.Source = "admin";
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { rate.Id, rate.CurrencyCode, rate.Rate, rate.Source });
        });

        // ---- product plans: read-only view of scraper-imported plans, options, quantity tiers ----
        admin.MapGet("/products/{id:guid}/plans", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var plans = await db.CasePlans.AsNoTracking()
                .Where(p => p.ProductId == id)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken);
            var planIds = plans.Select(p => p.Id).ToList();
            var options = await db.CasePlanOptions.AsNoTracking()
                .Where(o => planIds.Contains(o.PlanId))
                .ToListAsync(cancellationToken);
            var quantities = await db.QuantityOptions.AsNoTracking()
                .Where(q => q.ProductId == id)
                .OrderBy(q => q.Quantity)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                plans = plans.Select(p => new
                {
                    p.Id,
                    p.Title,
                    p.MinQuantity,
                    p.MaxQuantity,
                    p.CanUseCoupon,
                    p.CanUseCoin,
                    p.CashbackBps,
                    p.Enabled,
                    options = options.Where(o => o.PlanId == p.Id).Select(o => new
                    {
                        o.Id,
                        o.Quantity,
                        o.MinQuantity,
                        o.PriceCents,
                        o.UpliftBps,
                        o.Enabled
                    })
                }),
                quantityOptions = quantities.Select(q => new { q.Id, q.Quantity, q.PriceCents, q.AlarmThreshold })
            });
        });

        // ---- orders: prize view ----
        admin.MapGet("/orders/{id:guid}/prizes", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.UserPrizes.AsNoTracking()
                .Where(p => p.OrderId == id)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken)));

        // ---- users: coin ledger and login history ----
        admin.MapGet("/users/{id:guid}/coin-logs", async (
            Guid id,
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.UserCoinLogs.AsNoTracking().Where(l => l.UserId == id).OrderByDescending(l => l.Id);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        admin.MapGet("/users/{id:guid}/login-logs", async (
            Guid id,
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var account = await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (account is null)
            {
                return Results.Problem("User was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            var query = db.UserLoginLogs.AsNoTracking()
                .Where(l => l.UserId == id || l.Account == account.Phone || l.Account == account.Email);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(l => l.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        // ---- roll rooms: list with prizes ----
        admin.MapGet("/roll/rooms", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.RollRooms.AsNoTracking().OrderByDescending(r => r.StartAtUtc ?? r.EndAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        admin.MapGet("/roll/rooms/{id:guid}/prizes", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.RollRoomPrizes.AsNoTracking()
                .Where(p => p.RoomId == id)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken)));

        // ---- stats: daily trend and product ranking ----
        admin.MapGet("/stats/trend", async (
            DoggoDbContext db,
            [FromQuery] int days = 14,
            CancellationToken cancellationToken = default) =>
        {
            var since = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(days, 1, 90) - 1));
            var orders = await db.UserOrders.AsNoTracking()
                .Where(o => o.CreatedAtUtc >= since)
                .Select(o => new { o.CreatedAtUtc, o.TotalCostCents })
                .ToListAsync(cancellationToken);
            var users = await db.UserAccounts.AsNoTracking()
                .Where(u => u.CreatedAtUtc >= since)
                .Select(u => u.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var trend = new List<object>();
            for (var day = since; day <= DateTime.UtcNow.Date; day = day.AddDays(1))
            {
                var d = day;
                trend.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    newUsers = users.Count(u => u.Date == d),
                    orders = orders.Count(o => o.CreatedAtUtc.Date == d),
                    revenueCents = orders.Where(o => o.CreatedAtUtc.Date == d).Sum(o => o.TotalCostCents)
                });
            }

            return Results.Ok(trend);
        });

        admin.MapGet("/stats/top-products", async (
            DoggoDbContext db,
            [FromQuery] int top = 10,
            CancellationToken cancellationToken = default) =>
        {
            var rows = await db.UserOrders.AsNoTracking()
                .Where(o => o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.ProductId)
                .Select(g => new { ProductId = g.Key, OrderCount = g.Count(), RevenueCents = g.Sum(o => o.TotalCostCents) })
                .ToListAsync(cancellationToken);

            var ids = rows.Select(r => r.ProductId).ToList();
            var names = await db.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

            return Results.Ok(rows
                .OrderByDescending(r => r.RevenueCents)
                .Take(Math.Clamp(top, 1, 50))
                .Select(r => new
                {
                    productId = r.ProductId,
                    productName = names.GetValueOrDefault(r.ProductId, string.Empty),
                    orderCount = r.OrderCount,
                    revenueCents = r.RevenueCents
                }));
        });

        // ---- funnel: four-step conversion from FunnelDailyStat (funnel design §8.1) ----
        admin.MapGet("/funnel", async (
            DoggoDbContext db,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] string? groupBy,
            [FromQuery] string? grain,
            [FromQuery] string? client,
            [FromQuery] string? channel,
            CancellationToken cancellationToken) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var toValue = to is { } toParam && toParam < today ? toParam : today.AddDays(-1);
            var fromValue = from is { } fromParam && fromParam < toValue ? fromParam : toValue.AddDays(-13);
            // At most 90 days per report, matching the stats-area clamp style.
            if (fromValue < toValue.AddDays(-89))
            {
                fromValue = toValue.AddDays(-89);
            }

            var query = db.FunnelDailyStats.AsNoTracking()
                .Where(s => s.Date >= fromValue && s.Date <= toValue);
            if (Enum.TryParse<AnalyticsClient>(client, ignoreCase: true, out var clientFilter))
            {
                query = query.Where(s => s.Client == clientFilter);
            }

            if (!string.IsNullOrWhiteSpace(channel))
            {
                query = query.Where(s => s.Channel == channel.Trim());
            }

            var stats = await query.OrderBy(s => s.Date).ToListAsync(cancellationToken);

            var mode = groupBy?.Trim().ToLowerInvariant();
            string KeyOf(FunnelDailyStat s) => mode switch
            {
                "client" => s.Client.ToString().ToLowerInvariant(),
                "channel" => s.Channel,
                _ => "all"
            };

            static decimal Rate(long uv, long firstUv) => firstUv <= 0 ? 0 : Math.Round((decimal)uv / firstUv, 4);

            var steps = new (string Name, string Label, Func<FunnelDailyStat, long> Uv)[]
            {
                ("page_view_product", "浏览商品", s => s.ViewProductUv),
                ("checkout_intent", "加购", s => s.CheckoutIntentUv),
                ("order_created", "下单", s => s.OrderCreatedUv),
                ("order_paid", "支付", s => s.OrderPaidUv)
            };

            var groups = stats
                .GroupBy(KeyOf)
                .Select(g =>
                {
                    var uv = steps.Select(step => g.Sum(step.Uv)).ToArray();
                    return new
                    {
                        key = g.Key,
                        steps = steps.Select((step, i) => new
                        {
                            step = step.Name,
                            label = step.Label,
                            uv = uv[i],
                            rate = i == 0 ? (uv[0] > 0 ? 1m : 0m) : Rate(uv[i], uv[0]),
                            dropRate = i == 0 ? 0m : 1m - Rate(uv[i], uv[0])
                        }).ToArray()
                    };
                })
                .OrderBy(g => g.key)
                .ToArray();

            var isWeek = string.Equals(grain, "week", StringComparison.OrdinalIgnoreCase);
            static string IsoWeek(DateOnly date) => $"{ISOWeek.GetYear(date):0}-W{ISOWeek.GetWeekOfYear(date):00}";
            var rows = stats
                .GroupBy(s => (Period: isWeek ? IsoWeek(s.Date) : s.Date.ToString("yyyy-MM-dd"), Client: KeyOf(s), Channel: s.Channel))
                .Select(g => new
                {
                    date = g.Key.Period,
                    client = g.Key.Client,
                    channel = g.Key.Channel,
                    viewProductUv = g.Sum(s => s.ViewProductUv),
                    checkoutIntentUv = g.Sum(s => s.CheckoutIntentUv),
                    orderCreatedUv = g.Sum(s => s.OrderCreatedUv),
                    orderPaidUv = g.Sum(s => s.OrderPaidUv)
                })
                .ToArray();

            return Results.Ok(new
            {
                from = fromValue.ToString("yyyy-MM-dd"),
                to = toValue.ToString("yyyy-MM-dd"),
                grain = isWeek ? "week" : "day",
                groupBy = mode ?? "none",
                groups,
                rows,
                receivedToday = await db.AnalyticsEvents.AsNoTracking()
                    .CountAsync(e => e.ReceivedAtUtc >= DateTime.UtcNow.Date, cancellationToken)
            });
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

public sealed record EnabledRequest(bool Enabled);

public sealed record NoticeUpsert(string Title, string Content, DateTime? StartAtUtc, DateTime? EndAtUtc);

public sealed record RateUpdateRequest(decimal Rate);
