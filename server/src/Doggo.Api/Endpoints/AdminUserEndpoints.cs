using System.Globalization;
using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.System;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

/// <summary>
/// Back-office user surface: paged listing, disable toggle, coin ledger and login
/// history, behind the shared Admin policy.
/// </summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/users", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.UserAccounts.AsNoTracking().OrderByDescending(u => u.CreatedAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new AdminUserView(u.Id, u.Phone, u.Email, u.Nickname, u.InviteCode, u.Status, u.IsAdmin, u.CreatedAtUtc))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });
        admin.MapPut("/users/{id:guid}/status", async (
            Guid id,
            [FromBody] UserStatusRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var account = await db.UserAccounts.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (account is null)
            {
                return Results.Problem("User was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            account.Status = request.Disabled ? UserStatus.Disabled : UserStatus.Active;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { account.Id, account.Status });
        });
        admin.MapGet("/users/{id:guid}/coin-logs", async (
            Guid id,
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.UserCoinLogs.AsNoTracking().Where(l => l.UserId == id).OrderByDescending(l => l.Id);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });
        admin.MapGet("/users/{id:guid}/login-logs", async (
            Guid id,
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var account = await db.UserAccounts.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (account is null)
            {
                return Results.Problem("User was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            var query = db.UserLoginLogs.AsNoTracking()
                .Where(l => l.UserId == id || l.Account == account.Phone || l.Account == account.Email);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(l => l.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });
        return admin;
    }

public sealed record AdminUserView(
    Guid Id, string Phone, string Email, string Nickname, string InviteCode, UserStatus Status, bool IsAdmin, DateTime CreatedAtUtc);
public sealed record UserStatusRequest(bool Disabled);
}
