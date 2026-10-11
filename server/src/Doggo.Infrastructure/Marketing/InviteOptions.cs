namespace Doggo.Infrastructure.Marketing;

/// <summary>Invite reward settings.</summary>
public sealed class InviteOptions
{
    public const string SectionName = "Invite";

    /// <summary>Coins the inviter earns when the invitee completes their first order.</summary>
    public long CoinReward { get; set; } = 500;
}
