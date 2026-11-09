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
/// Back-office product surface: catalog CRUD, plans, quantity tiers, case contents
/// and exchange-rate correction, behind the shared Admin policy.
/// </summary>
public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder admin)
    {
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
        return admin;
    }

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
public sealed record RateUpdateRequest(decimal Rate);
}
