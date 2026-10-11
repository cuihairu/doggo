using Doggo.Infrastructure.Cases;
using Microsoft.AspNetCore.Mvc;

namespace Doggo.Api.Endpoints;

public static class PrizeEndpoints
{
    public static void MapPrizeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users/{userId:guid}/prizes");

        group.MapGet("/", async (
            Guid userId,
            [FromServices] PrizeService prizes,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var result = await prizes.ListPrizesAsync(userId, page, pageSize, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/{prizeId:guid}", async (
            Guid userId,
            Guid prizeId,
            [FromServices] PrizeService prizes,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await prizes.GetPrizeAsync(userId, prizeId, cancellationToken));
            }
            catch (PrizeNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        group.MapGet("/{prizeId:guid}/logs", async (
            Guid userId,
            Guid prizeId,
            [FromServices] PrizeService prizes,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await prizes.ListLogsAsync(userId, prizeId, cancellationToken));
            }
            catch (PrizeNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });
    }
}
