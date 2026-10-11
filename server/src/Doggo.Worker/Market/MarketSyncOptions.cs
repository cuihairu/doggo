namespace Doggo.Worker.Market;

public sealed class MarketSyncOptions
{
    public const string SectionName = "Market:Sync";

    /// <summary>Master switch; the worker exits immediately when false.</summary>
    public bool Enabled { get; set; }

    /// <summary>Base delay between sync runs while healthy.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>Steam app ids to sync, e.g. 730 (CS2) and 570 (Dota 2).</summary>
    public string[] AppIds { get; set; } = ["730", "570"];

    /// <summary>Listings requested per vapor call (the control plane caps this at 100).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Upper bound on vapor pages consumed per run, so a huge catalog cannot stall one loop.</summary>
    public int MaxPagesPerRun { get; set; } = 5;

    /// <summary>Cap for the exponential backoff applied after failures.</summary>
    public int MaxBackoffSeconds { get; set; } = 1800;
}
