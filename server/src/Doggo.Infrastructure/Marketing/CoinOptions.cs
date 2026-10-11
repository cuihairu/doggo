namespace Doggo.Infrastructure.Marketing;

/// <summary>Coin expiry sweep settings.</summary>
public sealed class CoinOptions
{
    public const string SectionName = "Coin";

    /// <summary>How often the worker looks for expired coin batches, in seconds.</summary>
    public int SweepIntervalSeconds { get; set; } = 300;
}
