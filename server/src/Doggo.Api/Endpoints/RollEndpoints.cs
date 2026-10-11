using Doggo.Domain.Cases;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public static class RollEndpoints
{
    public static void MapRollEndpoints(this IEndpointRouteBuilder app)
    {
        var templates = app.MapGroup("/api/roll/templates");

        templates.MapGet("/", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            return Results.Ok(await db.RollRoomTemplates.AsNoTracking()
                .OrderBy(t => t.Title)
                .ToListAsync(cancellationToken));
        });

        templates.MapPost("/", async (
            [FromBody] CreateTemplateRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var template = new RollRoomTemplate
            {
                Title = request.Title,
                RuleType = request.RuleType,
                DurationMinutes = Math.Max(request.DurationMinutes, 1),
                EntryDeadlineMinutes = Math.Max(request.EntryDeadlineMinutes, 0),
                MaxPrizeValueCents = request.MaxPrizeValueCents
            };
            db.RollRoomTemplates.Add(template);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/roll/templates/{template.Id}", template);
        });

        var rooms = app.MapGroup("/api/roll/rooms");

        rooms.MapGet("/", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var running = await db.RollRooms.AsNoTracking()
                .Where(r => r.Status == RollRoomStatus.Running)
                .OrderBy(r => r.EndAtUtc)
                .ToListAsync(cancellationToken);
            return Results.Ok(await RoomsToViewsAsync(db, running, cancellationToken));
        });

        // Rewards are seeded per room here so the create/join/draw/claim flow can be
        // exercised without a separate administration surface.
        rooms.MapPost("/{id:guid}/prizes", async (
            Guid id,
            [FromBody] IReadOnlyList<RollPrizeRequest> request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            await AddPrizesAsync(db, id, request, cancellationToken);
            return Results.Ok(new { roomId = id, prizes = request.Count });
        });

        rooms.MapGet("/{id:guid}", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var room = await db.RollRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (room is null)
            {
                return Results.Problem($"Roll room {id} was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok((await RoomsToViewsAsync(db, [room], cancellationToken)).FirstOrDefault());
        });

        rooms.MapPost("/", async (
            [FromBody] CreateRoomRequest request,
            [FromServices] RollService rolls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await rolls.CreateRoomFromTemplateAsync(request.TemplateId, cancellationToken);
                return Results.Created($"/api/roll/rooms/{result.RoomId}", result);
            }
            catch (RollValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        rooms.MapPost("/{id:guid}/join", async (
            Guid id,
            [FromBody] JoinRoomRequest request,
            [FromServices] RollService rolls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await rolls.JoinRoomAsync(id, request.UserId, request.EntryRef, cancellationToken));
            }
            catch (RollRoomNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (RollValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        rooms.MapPost("/{id:guid}/draw", async (
            Guid id,
            [FromServices] RollService rolls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await rolls.DrawAsync(id, cancellationToken));
            }
            catch (RollRoomNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (RollValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        rooms.MapPost("/{id:guid}/claim", async (
            Guid id,
            [FromBody] ClaimRoomRequest request,
            [FromServices] RollService rolls,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await rolls.ClaimAsync(id, request.UserId, cancellationToken));
            }
            catch (RollRoomNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (RollValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });
    }

    private static async Task AddPrizesAsync(
        DoggoDbContext db, Guid roomId, IReadOnlyList<RollPrizeRequest> prizes, CancellationToken cancellationToken)
    {
        var room = await db.RollRooms.FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken)
                   ?? throw new RollRoomNotFoundException($"Roll room {roomId} was not found.");

        foreach (var prize in prizes)
        {
            db.RollRoomPrizes.Add(new RollRoomPrize
            {
                // Explicit ids: several Added entities with empty ids collide in the identity map.
                Id = Guid.CreateVersion7(),
                RoomId = room.Id,
                Type = prize.Type,
                CoinAmount = prize.CoinAmount,
                Weight = Math.Max(prize.Weight, 1),
                Title = prize.Title,
                ImageUrl = prize.ImageUrl
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<RollRoomView>> RoomsToViewsAsync(
        DoggoDbContext db, IReadOnlyList<RollRoom> rooms, CancellationToken cancellationToken)
    {
        if (rooms.Count == 0)
        {
            return [];
        }

        var roomIds = rooms.Select(r => r.Id).ToList();
        var prizes = await db.RollRoomPrizes.AsNoTracking()
            .Where(p => roomIds.Contains(p.RoomId))
            .ToListAsync(cancellationToken);
        var counts = await db.RollRoomParticipants.AsNoTracking()
            .Where(p => roomIds.Contains(p.RoomId))
            .GroupBy(p => p.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var countByRoom = counts.ToDictionary(c => c.RoomId, c => c.Count);

        return rooms.Select(room => new RollRoomView(
            room.Id,
            room.TemplateId,
            room.Title,
            room.RuleType,
            room.Status,
            room.InviteCode,
            room.CoverUrl,
            room.MaxPrizeValueCents,
            room.StartAtUtc,
            room.EndAtUtc,
            room.EntryDeadlineUtc,
            countByRoom.GetValueOrDefault(room.Id),
            prizes
                .Where(p => p.RoomId == room.Id)
                .Select(p => new RollPrizeView(p.Id, p.Type, p.Title, p.ImageUrl, p.Weight, p.CoinAmount))
                .ToList()))
            .ToList();
    }
}

public sealed record CreateTemplateRequest(
    string Title,
    RollRuleType RuleType,
    int DurationMinutes,
    int EntryDeadlineMinutes,
    long MaxPrizeValueCents);

public sealed record RollPrizeRequest(RollPrizeType Type, long CoinAmount, int Weight, string Title, string ImageUrl);

public sealed record CreateRoomRequest(Guid TemplateId);

public sealed record JoinRoomRequest(Guid UserId, string? EntryRef);

public sealed record ClaimRoomRequest(Guid UserId);
