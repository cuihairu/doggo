using System.Security.Claims;
using Doggo.Infrastructure.Marketing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Doggo.Api.Endpoints;

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
    }
}
