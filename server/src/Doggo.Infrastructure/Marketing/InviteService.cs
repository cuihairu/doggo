using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Marketing;

/// <summary>
/// Invite rewards: the inviter earns coins when the invitee completes their first order.
/// The log row doubles as the once-per-invitee guard, so callbacks that replay or worker
/// sweeps that re-run cannot double-pay.
/// </summary>
public sealed partial class InviteService(
    DoggoDbContext db,
    CoinService coins,
    IOptions<InviteOptions> options,
    ILogger<InviteService> logger)
{
    /// <summary>Pays the inviter once per invitee; a no-op when the invitee has no inviter
    /// or the reward was already paid.</summary>
    public async Task<bool> GrantRewardAsync(Guid inviteeUserId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var invitee = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == inviteeUserId, cancellationToken);
        if (invitee?.InvitedByUserId is not { } inviterId)
        {
            return false;
        }

        var reward = options.Value.CoinReward;
        if (reward <= 0)
        {
            return false;
        }

        // The (invitee-unique) log row is the guard: first completed order only.
        var alreadyPaid = await db.InviteRewardLogs.AnyAsync(l => l.InviteeUserId == inviteeUserId, cancellationToken);
        if (alreadyPaid)
        {
            return false;
        }

        // Coins and the log row save in one call, so a lost race rolls the coins back too.
        await coins.TrackGrantAsync(inviterId, reward, "invite_reward", inviteeUserId.ToString(), null, cancellationToken);
        db.InviteRewardLogs.Add(new InviteRewardLog
        {
            Id = Guid.CreateVersion7(),
            InviterUserId = inviterId,
            InviteeUserId = inviteeUserId,
            CoinReward = reward,
            Note = $"order:{orderId}"
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two concurrent first orders both pass the paid pre-check; the invitee-unique
            // index settles the race. If the invitee is paid now, this call is the loser and
            // returns already-paid (coins and log rolled back together); anything else is a
            // real store failure and keeps throwing.
            var paidByRival = await db.InviteRewardLogs.AsNoTracking()
                .AnyAsync(l => l.InviteeUserId == inviteeUserId, cancellationToken);
            if (paidByRival)
            {
                db.ChangeTracker.Clear();
                return false;
            }

            throw;
        }

        LogRewarded(inviterId, inviteeUserId, reward);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "inviter {InviterUserId} earned {Amount} coins from invitee {InviteeUserId}")]
    private partial void LogRewarded(Guid inviterUserId, Guid inviteeUserId, long amount);
}
