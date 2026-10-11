using System.Security.Claims;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public static class CouponEndpoints
{
    public static void MapCouponEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/coupons");

        // The coupon center: every enabled coupon still inside its claim window.
        group.MapGet("/", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            var list = await db.Coupons.AsNoTracking()
                .Where(c => c.Enabled
                            && c.DeletedAtUtc == null
                            && (c.ValidFromUtc == null || c.ValidFromUtc <= now)
                            && (c.ValidToUtc == null || c.ValidToUtc > now))
                .OrderByDescending(c => c.Id)
                .ToListAsync(cancellationToken);
            return Results.Ok(list);
        });

        group.MapPost("/{id:guid}/claim", async (
            Guid id,
            ClaimsPrincipal principal,
            [FromServices] CouponService coupons,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(await coupons.ClaimCouponAsync(userId, id, cancellationToken));
            }
            catch (CouponValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        group.MapGet("/mine", async (
            ClaimsPrincipal principal,
            [FromServices] CouponService coupons,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(await coupons.ListMyCouponsAsync(userId, cancellationToken));
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        app.MapPost("/api/redeem", async (
            [FromBody] RedeemRequest request,
            ClaimsPrincipal principal,
            [FromServices] CouponService coupons,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(await coupons.RedeemAsync(userId, request.Code, cancellationToken));
            }
            catch (CouponValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
}

public sealed record RedeemRequest(string Code);
