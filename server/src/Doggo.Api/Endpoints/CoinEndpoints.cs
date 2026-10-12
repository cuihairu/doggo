using System.Security.Claims;
using Doggo.Domain.Users;
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

public sealed record CoinLogDto(Guid Id, CoinLogType Type, long Delta, long BalanceAfter, string? Note, DateTime CreatedAtUtc);

public static class CoinEndpoints
{
    public static void MapCoinEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/coins");

        group.MapGet("/balance", async (
            ClaimsPrincipal principal,
            [FromServices] CoinService coins,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(await coins.GetBalanceAsync(userId, cancellationToken));
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        /// <summary>The caller's own coin ledger, newest first — the C-end counterpart of the admin drawer.</summary>
        group.MapGet("/logs", async (
            ClaimsPrincipal principal,
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var pageIndex = Math.Clamp(page, 1, 100_000);
            var pageLimit = Math.Clamp(pageSize, 1, 100);

            var query = db.UserCoinLogs.AsNoTracking()
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.CreatedAtUtc)
                .ThenByDescending(l => l.Id);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((pageIndex - 1) * pageLimit)
                .Take(pageLimit)
                .Select(l => new CoinLogDto(l.Id, l.Type, l.Delta, l.BalanceAfter, l.Note, l.CreatedAtUtc))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { total, page = pageIndex, pageSize = pageLimit, items });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
}
