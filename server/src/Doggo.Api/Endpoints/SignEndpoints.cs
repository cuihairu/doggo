using System.Security.Claims;
using Doggo.Infrastructure.Marketing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Doggo.Api.Endpoints;

public static class SignEndpoints
{
    public static void MapSignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sign");

        group.MapGet("/", async (
            ClaimsPrincipal principal,
            [FromServices] SignService signs,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(await signs.GetCycleAsync(userId, cancellationToken));
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        group.MapPost("/", async (
            ClaimsPrincipal principal,
            [FromServices] SignService signs,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(await signs.SignTodayAsync(userId, cancellationToken));
            }
            catch (SignValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        group.MapPost("/makeup", async (
            [FromBody] MakeupSignRequest request,
            ClaimsPrincipal principal,
            [FromServices] SignService signs,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(await signs.SignAsync(userId, request.Date, request.OrderNo, cancellationToken));
            }
            catch (SignValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
}

public sealed record MakeupSignRequest(DateOnly Date, string OrderNo);
