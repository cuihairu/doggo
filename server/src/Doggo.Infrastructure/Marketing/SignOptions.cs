namespace Doggo.Infrastructure.Marketing;

/// <summary>Daily sign-in reward settings.</summary>
public sealed class SignOptions
{
    public const string SectionName = "Sign";

    /// <summary>Coins each signed (or made-up) day credits.</summary>
    public long CoinReward { get; set; } = 50;
}
