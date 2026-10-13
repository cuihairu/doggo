namespace Doggo.Infrastructure.Analytics;

/// <summary>Configuration bound from the "Analytics" section (funnel design §10 #9).</summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>Event-detail retention in days; FunnelDailyStat rows are kept permanently.</summary>
    public int EventRetentionDays { get; set; } = 90;
}
