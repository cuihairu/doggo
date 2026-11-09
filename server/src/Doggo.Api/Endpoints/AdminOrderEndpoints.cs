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
/// Back-office order surface: paged listing, refund and the prize view, behind the
/// shared Admin policy.
/// </summary>
public static class AdminOrderEndpoints
{
    public static IEndpointRouteBuilder MapAdminOrderEndpoints(this IEndpointRouteBuilder admin)
    {
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
        admin.MapGet("/orders/{id:guid}/prizes", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.UserPrizes.AsNoTracking()
                .Where(p => p.OrderId == id)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken)));
        return admin;
    }

public sealed record RefundAdminRequest(string Reason);
}
