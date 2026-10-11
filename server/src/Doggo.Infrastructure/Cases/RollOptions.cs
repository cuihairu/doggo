namespace Doggo.Infrastructure.Cases;

/// <summary>Configuration for the roll room sweep cadence.</summary>
public sealed class RollOptions
{
    public const string SectionName = "Roll";

    /// <summary>How often the worker checks for due rooms and finished periods.</summary>
    public int WorkerIntervalSeconds { get; set; } = 30;
}
