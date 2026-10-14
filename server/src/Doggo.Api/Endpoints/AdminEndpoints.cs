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

        // ---- products: create and update ----
        // Absent optional fields fall back to defaults on create and are left unchanged on update.
        admin.MapPost("/products", async ([FromBody] ProductUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Name is required." });
            }

            var product = new Product
            {
                Id = Guid.CreateVersion7(),
                Type = request.Type ?? ProductType.Crate,
                Name = request.Name.Trim(),
                ClassId = request.ClassId ?? string.Empty,
                IconUrl = request.IconUrl ?? string.Empty,
                PurchasePriceCents = request.PurchasePriceCents ?? 0,
                ReferencePriceCents = request.ReferencePriceCents ?? 0,
                AdjustPriceCents = request.AdjustPriceCents ?? 0,
                FinalPriceCents = request.FinalPriceCents ?? 0,
                RegularPriceCents = request.RegularPriceCents ?? 0,
                Status = request.Status ?? ProductStatus.OffShelf,
                PlanFlags = request.PlanFlags ?? string.Empty,
                BuyNotice = request.BuyNotice,
                SortOrder = request.SortOrder ?? 0
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/admin/products/{product.Id}", new { product.Id });
        });

        admin.MapPut("/products/{id:guid}", async (
            Guid id,
            [FromBody] ProductUpsert request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (product is null)
            {
                return Results.Problem("Product was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            if (request.Type is not null)
            {
                product.Type = request.Type.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                product.Name = request.Name.Trim();
            }

            if (request.ClassId is not null)
            {
                product.ClassId = request.ClassId;
            }

            if (request.IconUrl is not null)
            {
                product.IconUrl = request.IconUrl;
            }

            if (request.PurchasePriceCents is not null)
            {
                product.PurchasePriceCents = request.PurchasePriceCents.Value;
            }

            if (request.ReferencePriceCents is not null)
            {
                product.ReferencePriceCents = request.ReferencePriceCents.Value;
            }

            if (request.AdjustPriceCents is not null)
            {
                product.AdjustPriceCents = request.AdjustPriceCents.Value;
            }

            if (request.FinalPriceCents is not null)
            {
                product.FinalPriceCents = request.FinalPriceCents.Value;
            }

            if (request.RegularPriceCents is not null)
            {
                product.RegularPriceCents = request.RegularPriceCents.Value;
            }

            if (request.Status is not null)
            {
                product.Status = request.Status.Value;
            }

            if (request.PlanFlags is not null)
            {
                product.PlanFlags = request.PlanFlags;
            }

            if (request.BuyNotice is not null)
            {
                product.BuyNotice = request.BuyNotice;
            }

            if (request.SortOrder is not null)
            {
                product.SortOrder = request.SortOrder.Value;
            }
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { product.Id, product.Status });
        });

        // ---- product plans / tiers / contents: full-set replace ----
        // Orders snapshot their prices and reference plans by a loose Guid, so historical
        // rows stay interpretable when an operator rewrites the current set.
        admin.MapPut("/products/{id:guid}/plans", async (
            Guid id,
            [FromBody] PlanReplaceRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return Results.Problem("Product was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            var existingPlans = await db.CasePlans.Where(p => p.ProductId == id).ToListAsync(cancellationToken);
            var existingPlanIds = existingPlans.Select(p => p.Id).ToList();
            db.CasePlanOptions.RemoveRange(
                await db.CasePlanOptions.Where(o => existingPlanIds.Contains(o.PlanId)).ToListAsync(cancellationToken));
            db.CasePlans.RemoveRange(existingPlans);

            foreach (var planRequest in request.Plans)
            {
                var plan = new CasePlan
                {
                    Id = Guid.CreateVersion7(),
                    ProductId = id,
                    Title = planRequest.Title,
                    MinQuantity = planRequest.MinQuantity,
                    MaxQuantity = planRequest.MaxQuantity,
                    CanUseCoupon = planRequest.CanUseCoupon,
                    CanUseCoin = planRequest.CanUseCoin,
                    CashbackBps = planRequest.CashbackBps,
                    Enabled = planRequest.Enabled
                };
                db.CasePlans.Add(plan);
                foreach (var optionRequest in planRequest.Options)
                {
                    db.CasePlanOptions.Add(new CasePlanOption
                    {
                        Id = Guid.CreateVersion7(),
                        PlanId = plan.Id,
                        UpliftBps = optionRequest.UpliftBps,
                        Quantity = optionRequest.Quantity,
                        MinQuantity = optionRequest.MinQuantity,
                        PriceCents = optionRequest.PriceCents,
                        Enabled = optionRequest.Enabled
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id, planCount = request.Plans.Count });
        });

        admin.MapPut("/products/{id:guid}/quantity-options", async (
            Guid id,
            [FromBody] QuantityOptionReplaceRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return Results.Problem("Product was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            db.QuantityOptions.RemoveRange(
                await db.QuantityOptions.Where(q => q.ProductId == id).ToListAsync(cancellationToken));
            foreach (var optionRequest in request.Options)
            {
                db.QuantityOptions.Add(new QuantityOption
                {
                    Id = Guid.CreateVersion7(),
                    ProductId = id,
                    Quantity = optionRequest.Quantity,
                    PriceCents = optionRequest.PriceCents,
                    AlarmThreshold = optionRequest.AlarmThreshold
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id, optionCount = request.Options.Count });
        });

        admin.MapPut("/products/{id:guid}/contents", async (
            Guid id,
            [FromBody] ContentReplaceRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id, cancellationToken))
            {
                return Results.Problem("Product was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            db.CaseContents.RemoveRange(
                await db.CaseContents.Where(c => c.ProductId == id).ToListAsync(cancellationToken));
            foreach (var contentRequest in request.Contents)
            {
                db.CaseContents.Add(new CaseContent
                {
                    Id = Guid.CreateVersion7(),
                    ProductId = id,
                    Title = contentRequest.Title,
                    Quality = contentRequest.Quality,
                    Color = contentRequest.Color ?? string.Empty,
                    ImageUrl = contentRequest.ImageUrl ?? string.Empty,
                    ReferencePriceCents = contentRequest.ReferencePriceCents,
                    SortOrder = contentRequest.SortOrder
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { id, contentCount = request.Contents.Count });
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

        // ---- site settings: key/value rows the console edits in place ----
        admin.MapGet("/settings", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.SiteSettings.AsNoTracking()
                .OrderBy(s => s.Key)
                .Select(s => new SiteSettingView(s.Key, s.Value, s.ValueType, s.Note))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total = items.Count, items });
        });

        admin.MapPut("/settings/{key}", async (string key, [FromBody] SiteSettingUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var keyError = SiteSettingRules.ValidateKey(key);
            if (keyError is not null)
            {
                return Results.Problem(keyError, statusCode: StatusCodes.Status400BadRequest);
            }

            var valueError = SiteSettingRules.ValidateValue(request.Value, request.ValueType);
            if (valueError is not null)
            {
                return Results.Problem(valueError, statusCode: StatusCodes.Status400BadRequest);
            }

            // One row per key: the console edits a setting in place, so an
            // existing row is overwritten rather than duplicated.
            var setting = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (setting is null)
            {
                setting = new SiteSetting { Id = Guid.CreateVersion7(), Key = key };
                db.SiteSettings.Add(setting);
            }

            setting.Value = request.Value;
            setting.ValueType = request.ValueType ?? "text";
            setting.Note = request.Note;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new SiteSettingView(setting.Key, setting.Value, setting.ValueType, setting.Note));
        });

        // ---- payment channels: non-secret channel config; secrets stay in env vars ----
        admin.MapGet("/payment-channels", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.PaymentChannels.AsNoTracking()
                .OrderBy(c => c.Provider)
                .Select(c => PaymentChannelView.From(c))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total = items.Count, items });
        });

        admin.MapPut("/payment-channels/{provider}", async (
            string provider,
            [FromBody] PaymentChannelUpsert request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var parsed)
                || parsed is PaymentProvider.None or PaymentProvider.Coin or PaymentProvider.Coupon
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem("Unknown payment provider.", statusCode: StatusCodes.Status400BadRequest);
            }

            var configError = PaymentChannelRules.Validate(request);
            if (configError is not null)
            {
                return Results.Problem(configError, statusCode: StatusCodes.Status400BadRequest);
            }

            // One row per provider: the console edits the channel in place.
            var channel = await db.PaymentChannels.FirstOrDefaultAsync(c => c.Provider == parsed, cancellationToken);
            if (channel is null)
            {
                channel = new PaymentChannel { Id = Guid.CreateVersion7(), Provider = parsed };
                db.PaymentChannels.Add(channel);
            }

            channel.MerchantId = request.MerchantId;
            channel.AppId = request.AppId ?? string.Empty;
            channel.FeeBps = request.FeeBps;
            channel.SecretRef = request.SecretRef ?? string.Empty;
            channel.Enabled = request.Enabled;
            channel.Note = request.Note;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(PaymentChannelView.From(channel));
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
            var contents = await db.CaseContents.AsNoTracking()
                .Where(c => c.ProductId == id)
                .OrderBy(c => c.SortOrder)
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
                quantityOptions = quantities.Select(q => new { q.Id, q.Quantity, q.PriceCents, q.AlarmThreshold }),
                contents = contents.Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.Quality,
                    c.Color,
                    c.ImageUrl,
                    c.ReferencePriceCents,
                    c.SortOrder
                })
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

public sealed record ShelfRequest(bool OnShelf);

/// <summary>Product form payload; absent optional fields default on create and are skipped on update.</summary>
public sealed record ProductUpsert(
    string Name,
    ProductType? Type = null,
    string? ClassId = null,
    string? IconUrl = null,
    long? PurchasePriceCents = null,
    long? ReferencePriceCents = null,
    long? AdjustPriceCents = null,
    long? FinalPriceCents = null,
    long? RegularPriceCents = null,
    ProductStatus? Status = null,
    string? PlanFlags = null,
    string? BuyNotice = null,
    int? SortOrder = null);

public sealed record PlanOptionUpsert(
    int UpliftBps,
    int Quantity,
    int MinQuantity,
    long PriceCents,
    bool Enabled = true);

public sealed record PlanUpsert(
    string Title,
    int MinQuantity,
    int MaxQuantity,
    bool CanUseCoupon,
    bool CanUseCoin,
    int CashbackBps,
    bool Enabled,
    IReadOnlyList<PlanOptionUpsert> Options);

public sealed record PlanReplaceRequest(IReadOnlyList<PlanUpsert> Plans);

public sealed record QuantityOptionUpsert(int Quantity, long PriceCents, int AlarmThreshold = 0);

public sealed record QuantityOptionReplaceRequest(IReadOnlyList<QuantityOptionUpsert> Options);

public sealed record ContentUpsert(
    string Title,
    CaseQuality Quality,
    string? Color,
    string? ImageUrl,
    long ReferencePriceCents,
    int SortOrder);

public sealed record ContentReplaceRequest(IReadOnlyList<ContentUpsert> Contents);

public sealed record RefundAdminRequest(string Reason);

public sealed record AdminUserView(
    Guid Id, string Phone, string Email, string Nickname, string InviteCode, UserStatus Status, bool IsAdmin, DateTime CreatedAtUtc);

public sealed record UserStatusRequest(bool Disabled);

public sealed record TemplateEnabledRequest(bool Enabled);

public sealed record EnabledRequest(bool Enabled);

public sealed record NoticeUpsert(string Title, string Content, DateTime? StartAtUtc, DateTime? EndAtUtc);

public sealed record RateUpdateRequest(decimal Rate);

public sealed record SiteSettingView(string Key, string Value, string ValueType, string? Note);

public sealed record SiteSettingUpsert(string Value, string? Note = null, string? ValueType = null);

/// <summary>
/// Site settings are free-form key/value rows, so the guard rails are on the
/// shape instead of a fixed key list: dotted lower-case keys, bounded values,
/// and a closed set of value types the console knows how to render.
/// </summary>
public static class SiteSettingRules
{
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 5000;
    public const int MaxNoteLength = 200;

    public static readonly string[] ValueTypes = ["text", "number", "bool", "json", "url"];

    public static string? ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return "Setting key must be 1-64 characters.";
        }

        foreach (var c in key)
        {
            var allowed = char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';
            if (!allowed)
            {
                return "Setting key accepts letters, digits, dot, underscore and dash only.";
            }
        }

        return null;
    }

    public static string? ValidateValue(string? value, string? valueType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Setting value must not be empty.";
        }

        if (value.Length > MaxValueLength)
        {
            return $"Setting value must be at most {MaxValueLength} characters.";
        }

        if (valueType is not null && !ValueTypes.Contains(valueType))
        {
            return $"Setting valueType must be one of {string.Join(", ", ValueTypes)}.";
        }

        return null;
    }
}

public sealed record PaymentChannelUpsert(
    string MerchantId,
    string? AppId = null,
    int FeeBps = 0,
    string? SecretRef = null,
    bool Enabled = true,
    string? Note = null);

public sealed record PaymentChannelView(
    string Provider,
    string MerchantId,
    string AppId,
    int FeeBps,
    string SecretRef,
    bool Enabled,
    string? Note)
{
    public static PaymentChannelView From(PaymentChannel channel) => new(
        channel.Provider.ToString().ToLowerInvariant(),
        channel.MerchantId,
        channel.AppId,
        channel.FeeBps,
        channel.SecretRef,
        channel.Enabled,
        channel.Note);
}

/// <summary>
/// Guard rails for channel rows: bounded ids, fee inside 0-10000 bps, and a secret
/// reference shaped like an env-var name — the secret value itself is never accepted
/// here, because sensitive configuration only travels through environment variables.
/// </summary>
public static class PaymentChannelRules
{
    public const int MaxIdLength = 64;
    public const int MaxFeeBps = 10_000;
    public const int MaxNoteLength = 200;

    public static string? Validate(PaymentChannelUpsert request)
    {
        if (string.IsNullOrWhiteSpace(request.MerchantId) || request.MerchantId.Length > MaxIdLength)
        {
            return "Merchant id must be 1-64 characters.";
        }

        if (request.AppId is { Length: > MaxIdLength })
        {
            return $"App id must be at most {MaxIdLength} characters.";
        }

        if (request.FeeBps is < 0 or > MaxFeeBps)
        {
            return $"Fee must be 0-{MaxFeeBps} basis points.";
        }

        if (request.SecretRef is { Length: > 0 })
        {
            if (request.SecretRef.Length > MaxIdLength)
            {
                return $"Secret ref must be at most {MaxIdLength} characters.";
            }

            foreach (var c in request.SecretRef)
            {
                var allowed = char.IsAsciiLetterOrDigit(c) || c == '_';
                if (!allowed)
                {
                    return "Secret ref accepts an env-var-like name (letters, digits, underscore) — never the secret value.";
                }
            }
        }

        if (request.Note is { Length: > MaxNoteLength })
        {
            return $"Note must be at most {MaxNoteLength} characters.";
        }

        return null;
    }
}

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
