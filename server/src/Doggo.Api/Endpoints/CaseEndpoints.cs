using Doggo.Domain.Cases;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Cases;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public static class CaseEndpoints
{
    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/group-buyings");

        group.MapPost("/", async (
            [FromBody] CreateBatchRequest request,
            [FromServices] GroupBuyingService batches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await batches.CreateBatchAsync(new CreateBatchCommand(
                    request.ProductId,
                    request.Lot,
                    request.Seats
                        .Select(s => new BatchSeatInput(s.CrateAssetId, s.KeyAssetId))
                        .ToList()), cancellationToken);
                return Results.Created($"/api/group-buyings/{result.BatchId}", result);
            }
            catch (CaseValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] Doggo.Infrastructure.Persistence.DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var batch = await db.GroupBuyings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
            if (batch is null)
            {
                return Results.Problem($"Batch {id} was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            var seats = await db.GroupBuyingSeats.AsNoTracking()
                .Where(s => s.GroupBuyingId == id)
                .OrderBy(s => s.SeatNo)
                .ToListAsync(cancellationToken);

            return Results.Ok(GroupBuyingView.From(batch, seats));
        });

        group.MapPost("/{id:guid}/claim", async (
            Guid id,
            [FromBody] ClaimSeatRequest request,
            [FromServices] GroupBuyingService batches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await batches.ClaimSeatAsync(new ClaimSeatCommand(id, request.OrderId), cancellationToken);
                return Results.Ok(result);
            }
            catch (BatchNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (BatchSoldOutException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/{id:guid}/seats/{seatNo:int}/paid", PaidOrReleased);
        group.MapPost("/{id:guid}/seats/{seatNo:int}/release", PaidOrReleased);

        group.MapPost("/{id:guid}/seats/{seatNo:int}/open", async (
            Guid id,
            int seatNo,
            [FromServices] GroupBuyingService batches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var jobId = await batches.SubmitOpeningAsync(new SubmitOpeningCommand(id, seatNo), cancellationToken);
                return Results.Ok(new { jobId });
            }
            catch (CaseOpeningUnavailableException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (BatchNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (SeatNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/{id:guid}/seats/{seatNo:int}/result", async (
            Guid id,
            int seatNo,
            [FromBody] OpeningResultRequest request,
            [FromServices] GroupBuyingService batches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var applied = await batches.ApplyOpeningResultAsync(new ApplyOpeningResultCommand(
                    id,
                    seatNo,
                    request.ToItem(),
                    request.HistoryId,
                    request.RawJson), cancellationToken);
                return Results.Ok(applied);
            }
            catch (BatchNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (SeatNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (CaseValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapPost("/{id:guid}/seats/{seatNo:int}/advance", async (
            Guid id,
            int seatNo,
            [FromBody] AdvanceSeatRequest request,
            [FromServices] GroupBuyingService batches,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await batches.AdvanceSeatAsync(id, seatNo, request.Target, cancellationToken);
                return Results.Ok(new { seatNo, status = (int)request.Target });
            }
            catch (BatchNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (SeatNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
    }

    /// <summary>Shared handler for the paid and release transitions (same shape, no body).</summary>
    private static async Task<IResult> PaidOrReleased(
        Guid id,
        int seatNo,
        HttpRequest request,
        [FromServices] GroupBuyingService batches,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Path.Value!.EndsWith("/paid", StringComparison.Ordinal))
            {
                await batches.MarkSeatPaidAsync(id, seatNo, cancellationToken);
            }
            else
            {
                await batches.ReleaseSeatAsync(id, seatNo, cancellationToken);
            }

            return Results.Ok(new { seatNo });
        }
        catch (BatchNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (SeatNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

public sealed record CreateBatchRequest(Guid ProductId, string Lot, IReadOnlyList<BatchSeatRequest> Seats);

public sealed record BatchSeatRequest(string CrateAssetId, string? KeyAssetId);

public sealed record ClaimSeatRequest(Guid OrderId);

public sealed record OpeningResultRequest(
    string AssetId,
    string? ClassId,
    string? InstanceId,
    string? MarketHashName,
    CaseQuality Quality,
    string? Exterior,
    double? FloatValue,
    string? Rarity,
    string? IconUrl,
    string? InspectLink,
    long PriceCents,
    DateTime? TradableAtUtc,
    long? HistoryId,
    string? RawJson)
{
    public OpeningResultItem ToItem() => new(
        AssetId,
        ClassId ?? string.Empty,
        InstanceId ?? string.Empty,
        MarketHashName ?? string.Empty,
        Quality,
        Exterior ?? string.Empty,
        FloatValue,
        Rarity ?? string.Empty,
        IconUrl ?? string.Empty,
        InspectLink,
        PriceCents,
        TradableAtUtc);
}

public sealed record AdvanceSeatRequest(SeatStatus Target);

public sealed record SeatView(
    int SeatNo,
    SeatStatus Status,
    string? CrateAssetId,
    string? KeyAssetId,
    string? PrizeAssetId,
    Guid? OrderId)
{
    public static SeatView From(GroupBuyingSeat seat) => new(
        seat.SeatNo,
        seat.Status,
        seat.CrateAssetId,
        seat.KeyAssetId,
        seat.PrizeAssetId,
        seat.OrderId);
}

public sealed record GroupBuyingView(
    Guid Id,
    Guid ProductId,
    string Lot,
    int SeatCount,
    int SoldCount,
    GroupBuyingStatus Status,
    long Version,
    IReadOnlyList<SeatView> Seats)
{
    public static GroupBuyingView From(GroupBuying batch, IReadOnlyList<GroupBuyingSeat> seats) => new(
        batch.Id,
        batch.ProductId,
        batch.Lot,
        batch.SeatCount,
        batch.SoldCount,
        batch.Status,
        batch.Version,
        seats.Select(SeatView.From).ToList());
}
