using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Infrastructure.Users;

public sealed class TradeUrlNotFoundException(string message) : Exception(message);

public sealed class TradeUrlConflictException(string message) : Exception(message);

public sealed class TradeUrlValidationException(string message) : Exception(message);

public sealed record TradeUrlCheckResult(bool Valid, string? SteamId, string? Partner, string? Token, string? Error);

public sealed record AddTradeUrlCommand(Guid UserId, string Url, string? Nickname = null);

public sealed record UpdateTradeUrlCommand(string? Url, string? Nickname, bool? IsPreferred);

/// <summary>
/// Per-user Steam trade links. Adding and updating reparse the URL and reset validation,
/// so a stale or edited link never feeds a retrieval dispatch unchecked.
/// </summary>
public sealed class TradeUrlService(DoggoDbContext db)
{
    public async Task<List<UserTradeUrl>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.UserTradeUrls
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.IsPreferred)
            .ThenByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<UserTradeUrl> AddAsync(AddTradeUrlCommand command, CancellationToken cancellationToken = default)
    {
        var parts = Parse(command.Url);

        var duplicate = await db.UserTradeUrls
            .FirstOrDefaultAsync(t => t.UserId == command.UserId && t.Url == command.Url.Trim(), cancellationToken);
        if (duplicate is not null)
        {
            throw new TradeUrlConflictException($"This trade URL is already added for user {command.UserId}.");
        }

        var link = new UserTradeUrl
        {
            Id = Guid.CreateVersion7(),
            UserId = command.UserId,
            SteamId = parts.SteamId,
            Partner = parts.Partner,
            Token = parts.Token,
            Url = command.Url.Trim(),
            Nickname = command.Nickname?.Trim() ?? string.Empty,
            IsPreferred = await db.UserTradeUrls.AnyAsync(t => t.UserId == command.UserId, cancellationToken) == false
        };

        db.UserTradeUrls.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<UserTradeUrl> UpdateAsync(
        Guid userId,
        Guid id,
        UpdateTradeUrlCommand command,
        CancellationToken cancellationToken = default)
    {
        var link = await FindAsync(userId, id, cancellationToken);

        if (command.Url is not null)
        {
            var parts = Parse(command.Url);
            var duplicate = await db.UserTradeUrls
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Id != id && t.Url == command.Url.Trim(), cancellationToken);
            if (duplicate is not null)
            {
                throw new TradeUrlConflictException($"This trade URL is already added for user {userId}.");
            }

            link.Url = command.Url.Trim();
            link.SteamId = parts.SteamId;
            link.Partner = parts.Partner;
            link.Token = parts.Token;
            ResetCheck(link);
        }

        if (command.Nickname is not null)
        {
            link.Nickname = command.Nickname.Trim();
        }

        if (command.IsPreferred is true)
        {
            var others = await db.UserTradeUrls
                .Where(t => t.UserId == userId && t.Id != id)
                .ToListAsync(cancellationToken);
            foreach (var other in others.Where(t => t.IsPreferred))
            {
                other.IsPreferred = false;
            }

            link.IsPreferred = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var link = await db.UserTradeUrls.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
        if (link is null)
        {
            return false;
        }

        db.UserTradeUrls.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Validates a link by parsing it; a real Steam reachability check needs the trade
    /// server and lands with the vapor trading integration.
    /// </summary>
    public async Task<(UserTradeUrl Link, TradeUrlCheckResult Result)> CheckAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var link = await FindAsync(userId, id, cancellationToken);

        if (SteamTradeUrl.TryParse(link.Url, out var parts))
        {
            link.SteamId = parts.SteamId;
            link.Partner = parts.Partner;
            link.Token = parts.Token;
            link.Status = TradeUrlStatus.Valid;
            link.CheckError = null;
            link.CheckedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return (link, new TradeUrlCheckResult(true, parts.SteamId, parts.Partner, parts.Token, null));
        }

        link.Status = TradeUrlStatus.Invalid;
        link.CheckError = "trade URL is malformed or missing partner/token";
        link.CheckedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return (link, new TradeUrlCheckResult(false, null, null, null, link.CheckError));
    }

    public static TradeUrlCheckResult CheckUrl(string url)
    {
        if (SteamTradeUrl.TryParse(url, out var parts))
        {
            return new TradeUrlCheckResult(true, parts.SteamId, parts.Partner, parts.Token, null);
        }

        return new TradeUrlCheckResult(false, null, null, null, "trade URL must look like https://steamcommunity.com/tradeoffer/new/?partner=123&token=abcd123");
    }

    private async Task<UserTradeUrl> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.UserTradeUrls.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
        ?? throw new TradeUrlNotFoundException($"Trade URL {id} was not found for user {userId}.");

    private static TradeUrlParts Parse(string url)
    {
        if (SteamTradeUrl.TryParse(url, out var parts))
        {
            return parts;
        }

        throw new TradeUrlValidationException("trade URL is malformed or missing partner/token");
    }

    private static void ResetCheck(UserTradeUrl link)
    {
        link.Status = TradeUrlStatus.Unchecked;
        link.CheckError = null;
        link.CheckedAtUtc = null;
    }
}
