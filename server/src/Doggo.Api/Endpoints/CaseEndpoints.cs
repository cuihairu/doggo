using System.Security.Claims;
using Doggo.Domain.Cases;
using Doggo.Domain.Products;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public static class CaseEndpoints
{
    /// <summary>True when the order belongs to the caller (no claiming a seat with someone else's order).</summary>
    private static async Task<bool> OrderOwnedByAsync(
        DoggoDbContext db, Guid orderId, Guid userId, CancellationToken cancellationToken) =>
        await db.UserOrders.AsNoTracking().AnyAsync(o => o.Id == orderId && o.UserId == userId, cancellationToken);

    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/group-buyings");

        // Creating a group-buying batch is a back-office inventory action: a regular user
        // could otherwise mint seats that do not exist.
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
                    request.VaporCaseId,
                    request.Seats
                        .Select(s => new BatchSeatInput(s.CrateAssetId, s.KeyAssetId))
                        .ToList()), cancellationToken);
                return Results.Created($"/api/group-buyings/{result.BatchId}", result);
            }
            catch (CaseValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }).RequireAuthorization("Admin");

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

            var orderIds = seats.Select(s => s.OrderId).Where(o => o.HasValue).Select(o => o!.Value).Distinct().ToList();
            var prizes = orderIds.Count > 0
                ? await db.UserPrizes.AsNoTracking()
                    .Where(p => orderIds.Contains(p.OrderId))
                    .ToListAsync(cancellationToken)
                : [];
            // UserPrize idempotency is per (order, asset); a legitimate replay can therefore
            // seed two rows on one order. Grouping by (order, asset) and matching each seat on
            // its own PrizeAssetId keeps a multi-seat order from folding distinct prizes into one.
            var prizesByOrderAndAsset = prizes
                .GroupBy(p => (p.OrderId, p.AssetId))
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAtUtc).First());

            return Results.Ok(GroupBuyingView.From(batch, seats, prizesByOrderAndAsset));
        });

        group.MapPost("/{id:guid}/claim", async (
            Guid id,
            [FromBody] ClaimSeatRequest request,
            ClaimsPrincipal principal,
            [FromServices] GroupBuyingService batches,
            [FromServices] DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.CurrentUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                if (!await OrderOwnedByAsync(db, request.OrderId, userId.Value, cancellationToken))
                {
                    return Results.NotFound();
                }

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

        // Seat inventory corrections (a seat paid off-system, a stuck seat released) are
        // back-office actions, not user mutations: only admins drive them, and the owner
        // check is skipped because the admin is not the seat's buyer.
        group.MapPost("/{id:guid}/seats/{seatNo:int}/paid", PaidOrReleased)
            .RequireAuthorization("Admin");
        group.MapPost("/{id:guid}/seats/{seatNo:int}/release", PaidOrReleased)
            .RequireAuthorization("Admin");

        group.MapPost("/{id:guid}/seats/{seatNo:int}/open", async (
            Guid id,
            int seatNo,
            ClaimsPrincipal principal,
            [FromServices] GroupBuyingService batches,
            [FromServices] DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = principal.CurrentUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var jobId = await batches.SubmitOpeningAsync(new SubmitOpeningCommand(id, seatNo), userId.Value, cancellationToken);
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
            catch (SeatConcurrencyException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
            catch (VaporClientException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // The drop (opening_result) and the post-open chain (stock_fetch_started, stock_ready,
        // trade_started, trade_completed) are driven by the vapor webhook, not by the user who
        // opened the seat. A client-supplied result would let a user mint any prize for a seat
        // they did not actually win, so those user endpoints are gone.
    }

    /// <summary>Shared handler for the paid and release transitions (same shape, no body).</summary>
    private static async Task<IResult> PaidOrReleased(
        Guid id,
        int seatNo,
        HttpRequest request,
        [FromServices] GroupBuyingService batches,
        [FromServices] DoggoDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.Path.Value!.EndsWith("/paid", StringComparison.Ordinal))
            {
                // Admin operation: no owner check (the admin is not the seat's buyer).
                await batches.MarkSeatPaidAsync(id, seatNo, userId: null, cancellationToken);
            }
            else
            {
                await batches.ReleaseSeatAsync(id, seatNo, userId: null, cancellationToken);
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
        catch (SeatConcurrencyException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

public sealed record CreateBatchRequest(Guid ProductId, string Lot, string VaporCaseId, IReadOnlyList<BatchSeatRequest> Seats);

public sealed record BatchSeatRequest(string CrateAssetId, string? KeyAssetId);

public sealed record ClaimSeatRequest(Guid OrderId);

public sealed record PrizeView(
    string AssetId,
    string ClassId,
    string InstanceId,
    string MarketHashName,
    CaseQuality Quality,
    string Exterior,
    double? FloatValue,
    string Rarity,
    string IconUrl,
    string? InspectLink,
    long PriceCents,
    DateTime? TradableAtUtc)
{
    public static PrizeView From(UserPrize prize) => new(
        prize.AssetId,
        prize.ClassId,
        prize.InstanceId,
        prize.MarketHashName,
        prize.Quality,
        prize.Exterior,
        prize.FloatValue,
        prize.Rarity,
        prize.IconUrl,
        prize.InspectLink,
        prize.PriceCents,
        prize.TradableAtUtc);
}

public sealed record SeatView(
    int SeatNo,
    SeatStatus Status,
    string? CrateAssetId,
    string? KeyAssetId,
    string? PrizeAssetId,
    Guid? OrderId,
    PrizeView? Prize)
{
    public static SeatView From(GroupBuyingSeat seat, PrizeView? prize = null) => new(
        seat.SeatNo,
        seat.Status,
        seat.CrateAssetId,
        seat.KeyAssetId,
        seat.PrizeAssetId,
        seat.OrderId,
        prize);
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
    public static GroupBuyingView From(
        GroupBuying batch,
        IReadOnlyList<GroupBuyingSeat> seats,
        IReadOnlyDictionary<(Guid OrderId, string AssetId), UserPrize> prizesByOrderAndAsset) => new(
        batch.Id,
        batch.ProductId,
        batch.Lot,
        batch.SeatCount,
        batch.SoldCount,
        batch.Status,
        batch.Version,
        seats.Select(s => SeatView.From(
            s,
            s.OrderId is { } orderId && s.PrizeAssetId is { } assetId
                && prizesByOrderAndAsset.TryGetValue((orderId, assetId), out var prize)
                ? PrizeView.From(prize)
                : null)).ToList());
}
