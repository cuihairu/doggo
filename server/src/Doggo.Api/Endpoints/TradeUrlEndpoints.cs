using Doggo.Domain.Users;
using Doggo.Infrastructure.Users;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Doggo.Api.Endpoints;

public static class TradeUrlEndpoints
{
    public static void MapTradeUrlEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users/{userId:guid}/trade-urls");

        group.MapGet("/", async (
            Guid userId,
            [FromServices] TradeUrlService tradeUrls,
            CancellationToken cancellationToken) =>
        {
            var links = await tradeUrls.ListAsync(userId, cancellationToken);
            return Results.Ok(links.Select(TradeUrlView.From));
        });

        group.MapPost("/", async (
            Guid userId,
            [FromBody] AddTradeUrlRequest request,
            [FromServices] TradeUrlService tradeUrls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var link = await tradeUrls.AddAsync(new AddTradeUrlCommand(userId, request.Url, request.Nickname), cancellationToken);
                return Results.Created($"/api/users/{userId}/trade-urls/{link.Id}", TradeUrlView.From(link));
            }
            catch (TradeUrlValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (TradeUrlConflictException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPut("/{id:guid}", async (
            Guid userId,
            Guid id,
            [FromBody] UpdateTradeUrlRequest request,
            [FromServices] TradeUrlService tradeUrls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var link = await tradeUrls.UpdateAsync(userId, id, new UpdateTradeUrlCommand(request.Url, request.Nickname, request.IsPreferred), cancellationToken);
                return Results.Ok(TradeUrlView.From(link));
            }
            catch (TradeUrlNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (TradeUrlValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (TradeUrlConflictException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapDelete("/{id:guid}", async (
            Guid userId,
            Guid id,
            [FromServices] TradeUrlService tradeUrls,
            CancellationToken cancellationToken) =>
        {
            var deleted = await tradeUrls.DeleteAsync(userId, id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        group.MapPost("/{id:guid}/check", async (
            Guid userId,
            Guid id,
            [FromServices] TradeUrlService tradeUrls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (_, result) = await tradeUrls.CheckAsync(userId, id, cancellationToken);
                return Results.Ok(result);
            }
            catch (TradeUrlNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        app.MapPost("/api/trade-urls/validate", (
            [FromBody] ValidateTradeUrlRequest request) =>
        {
            return Results.Ok(TradeUrlService.CheckUrl(request.Url));
        });
    }
}

public sealed record AddTradeUrlRequest(string Url, string? Nickname);

public sealed record UpdateTradeUrlRequest(string? Url, string? Nickname, bool? IsPreferred);

public sealed record ValidateTradeUrlRequest(string Url);

public sealed record TradeUrlView(
    Guid Id,
    Guid UserId,
    string SteamId,
    string Partner,
    string Token,
    string Url,
    string Nickname,
    bool IsPreferred,
    TradeUrlStatus Status,
    string? CheckError,
    DateTime? CheckedAtUtc,
    DateTime CreatedAtUtc)
{
    public static TradeUrlView From(UserTradeUrl link) => new(
        link.Id,
        link.UserId,
        link.SteamId,
        link.Partner,
        link.Token,
        link.Url,
        link.Nickname,
        link.IsPreferred,
        link.Status,
        link.CheckError,
        link.CheckedAtUtc,
        link.CreatedAtUtc);
}
