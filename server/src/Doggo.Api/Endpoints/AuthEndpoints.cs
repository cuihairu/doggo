using System.Security.Claims;
using Doggo.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Doggo.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (
            [FromBody] RegisterRequest request,
            HttpContext context,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await auth.RegisterAsync(
                    new RegisterCommand(request.Account, request.Password, request.Nickname, request.InviteCode),
                    context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    context.Request.Headers.UserAgent.ToString(),
                    cancellationToken);
                return Results.Created($"/api/auth/me", result);
            }
            catch (AuthValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AccountExistsException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            HttpContext context,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auth.LoginAsync(
                    request.Account,
                    request.Password,
                    context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    context.Request.Headers.UserAgent.ToString(),
                    cancellationToken));
            }
            catch (AuthValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/wechat", async (
            [FromBody] WechatLoginRequest request,
            HttpContext context,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auth.LoginWithWechatAsync(
                    request.Code,
                    request.Nickname,
                    context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    context.Request.Headers.UserAgent.ToString(),
                    cancellationToken));
            }
            catch (AuthValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (WechatLoginNotConfiguredException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (WechatLoginException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest request,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await auth.RefreshAsync(request.RefreshToken, cancellationToken));
            }
            catch (AuthValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        group.MapPost("/logout", async (
            [FromBody] RefreshRequest request,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            await auth.LogoutAsync(request.RefreshToken, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                return Results.Ok(await auth.GetProfileAsync(userId, cancellationToken));
            }
            catch (AuthException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        var steam = app.MapGroup("/api/auth/steam");

        // The browser opens this URL; the user logs in on steamcommunity.com and Steam
        // redirects back to /api/auth/steam/callback.
        steam.MapGet("/login", (
            ClaimsPrincipal principal,
            [FromServices] SteamOpenIdService openId,
            [FromServices] IOptions<AuthOptions> options) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
            {
                return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var baseUrl = options.Value.PublicBaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return Results.Problem("Auth:PublicBaseUrl is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Redirect(openId.BuildLoginUrl(userId, baseUrl));
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });

        steam.MapGet("/callback", async (
            HttpContext context,
            [FromServices] SteamOpenIdService openId,
            [FromServices] AuthService auth,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query
                .Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString()))
                .ToList();
            var bound = await openId.VerifyCallbackAsync(query, cancellationToken);
            if (bound is null)
            {
                return Results.Problem("Steam could not verify this login.", statusCode: StatusCodes.Status401Unauthorized);
            }

            try
            {
                var profile = await auth.BindSteamAsync(bound.UserId, bound.SteamId, cancellationToken);
                return Results.Ok(new { bound.UserId, bound.SteamId, profileId = profile.Id });
            }
            catch (AuthValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
    }
}

public sealed record RegisterRequest(string Account, string Password, string? Nickname, string? InviteCode);

public sealed record LoginRequest(string Account, string Password);

public sealed record WechatLoginRequest(string Code, string? Nickname);

public sealed record RefreshRequest(string RefreshToken);
