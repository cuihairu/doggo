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

        await coins.GrantAsync(inviterId, reward, "invite_reward", inviteeUserId.ToString(), null, cancellationToken);
        db.InviteRewardLogs.Add(new InviteRewardLog
        {
            Id = Guid.CreateVersion7(),
            InviterUserId = inviterId,
            InviteeUserId = inviteeUserId,
            CoinReward = reward,
            Note = $"order:{orderId}"
        });
        await db.SaveChangesAsync(cancellationToken);
        LogRewarded(inviterId, inviteeUserId, reward);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "inviter {InviterUserId} earned {Amount} coins from invitee {InviteeUserId}")]
    private partial void LogRewarded(Guid inviterUserId, Guid inviteeUserId, long amount);
}
