using System.Security.Claims;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

public sealed record InviteRecordDto(Guid Id, string InviteeNickname, long CoinReward, DateTime CreatedAtUtc);

/// <summary>Invite rewards the caller has earned; each row is one rewarded invitee.</summary>
public static class InviteEndpoints
{
    public static void MapInviteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/invites");

        group.MapGet("/", async (
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

            var rewards = db.InviteRewardLogs.AsNoTracking().Where(l => l.InviterUserId == userId);
            var total = await rewards.CountAsync(cancellationToken);
            var totalReward = await rewards.SumAsync(l => l.CoinReward, cancellationToken);

            var logs = await rewards
                .OrderByDescending(l => l.CreatedAtUtc)
                .ThenByDescending(l => l.Id)
                .Skip((pageIndex - 1) * pageLimit)
                .Take(pageLimit)
                .ToListAsync(cancellationToken);

            var inviteeIds = logs.Select(l => l.InviteeUserId).ToList();
            var nicknames = await db.UserAccounts.AsNoTracking()
                .Where(u => inviteeIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nickname, cancellationToken);

            var items = logs
                .Select(log => new InviteRecordDto(
                    log.Id,
                    nicknames.TryGetValue(log.InviteeUserId, out var nickname) ? nickname : string.Empty,
                    log.CoinReward,
                    log.CreatedAtUtc))
                .ToList();

            return Results.Ok(new { total, totalReward, page = pageIndex, pageSize = pageLimit, items });
        })
        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }
}
