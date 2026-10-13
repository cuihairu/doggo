using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Analytics;

/// <summary>
/// Turns AnalyticsEvent rows into FunnelDailyStat rows (funnel design §7).
///
/// An instance opens on a page_view_product and consumes later steps strictly in funnel
/// order, each event going to the most recent open instance of the same identity; an event
/// never feeds two instances. Identity is UserId, falling back to VisitorKey, with session
/// backfill (§10.3): anonymous events in a session that contains any logged-in event adopt
/// that UserId. Output rows are keyed (first-step date, client, channel) and upserted, so
/// rerunning a window overwrites instead of duplicating.
/// </summary>
public sealed partial class FunnelAggregator(DoggoDbContext db, ILogger<FunnelAggregator> logger)
{
    private static readonly string[] FunnelSteps = ["page_view_product", "checkout_intent", "order_created", "order_paid"];

    /// <summary>Later steps must land within 7 days of the first step (§7.3).</summary>
    private static readonly TimeSpan FunnelWindow = TimeSpan.FromDays(7);

    /// <summary>Aggregates [from, to] by first-step day; returns the number of stat rows written.</summary>
    public async Task<int> AggregateRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var windowStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // Instances can consume steps up to 7 days after opening, so late days pull extra days.
        var windowEnd = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1).Add(FunnelWindow);

        var events = await db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.OccurredAtUtc >= windowStart && e.OccurredAtUtc < windowEnd && FunnelSteps.Contains(e.EventName))
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        // Session backfill (§10.3): one logged-in event unifies the whole session's anonymous events.
        var sessionOwners = events
            .Where(e => e.SessionId != Guid.Empty && e.UserId is not null)
            .GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => g.First().UserId!.Value);

        var instances = new List<FunnelInstance>();
        var openByIdentity = new Dictionary<string, List<FunnelInstance>>();

        foreach (var e in events)
        {
            if (sessionOwners.TryGetValue(e.SessionId, out var owner))
            {
                e.UserId = owner;
            }

            var identity = e.UserId?.ToString() ?? $"vk:{e.VisitorKey}";

            if (e.EventName == FunnelSteps[0])
            {
                var instance = new FunnelInstance(identity, e.OccurredAtUtc, e.Client, e.Channel);
                instances.Add(instance);
                if (!openByIdentity.TryGetValue(identity, out var open))
                {
                    openByIdentity[identity] = open = [];
                }

                open.Add(instance);
                continue;
            }

            var stepIndex = Array.IndexOf(FunnelSteps, e.EventName);
            var target = LastOpenInstance(openByIdentity, identity, e.OccurredAtUtc);
            if (target is not null && target.NextStep == stepIndex && e.OccurredAtUtc < target.StartUtc.Add(FunnelWindow))
            {
                target.Consume(stepIndex);
            }
        }

        return await WriteStatsAsync(instances, from, to, cancellationToken);
    }

    private static FunnelInstance? LastOpenInstance(Dictionary<string, List<FunnelInstance>> openByIdentity, string identity, DateTime at)
    {
        if (!openByIdentity.TryGetValue(identity, out var open))
        {
            return null;
        }

        FunnelInstance? latest = null;
        foreach (var instance in open)
        {
            if (instance.StartUtc <= at && at < instance.StartUtc.Add(FunnelWindow))
            {
                latest = instance;
            }
        }

        return latest;
    }

    private async Task<int> WriteStatsAsync(
        IReadOnlyList<FunnelInstance> instances, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var uvs = new Dictionary<(DateOnly Date, AnalyticsClient Client, string Channel), FunnelAccumulator>();
        foreach (var instance in instances)
        {
            var key = (DateOnly.FromDateTime(instance.StartUtc), instance.Client, instance.Channel);
            if (!uvs.TryGetValue(key, out var acc))
            {
                uvs[key] = acc = new FunnelAccumulator();
            }

            acc.View.Add(instance.Identity);
            for (var step = 1; step < FunnelSteps.Length; step++)
            {
                if (instance.ReachedSteps.Contains(step))
                {
                    acc.Steps[step].Add(instance.Identity);
                }
            }
        }

        if (uvs.Count == 0)
        {
            return 0;
        }

        var existing = await db.FunnelDailyStats
            .Where(s => s.Date >= from && s.Date <= to)
            .ToListAsync(cancellationToken);

        foreach (var (key, acc) in uvs)
        {
            var stat = existing.FirstOrDefault(s => s.Date == key.Date && s.Client == key.Client && s.Channel == key.Channel);
            if (stat is null)
            {
                db.FunnelDailyStats.Add(new FunnelDailyStat
                {
                    Id = Guid.CreateVersion7(),
                    Date = key.Date,
                    Client = key.Client,
                    Channel = key.Channel,
                    ViewProductUv = acc.View.Count,
                    CheckoutIntentUv = acc.Steps[1].Count,
                    OrderCreatedUv = acc.Steps[2].Count,
                    OrderPaidUv = acc.Steps[3].Count
                });
            }
            else
            {
                stat.ViewProductUv = acc.View.Count;
                stat.CheckoutIntentUv = acc.Steps[1].Count;
                stat.OrderCreatedUv = acc.Steps[2].Count;
                stat.OrderPaidUv = acc.Steps[3].Count;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogAggregated(logger, uvs.Count, instances.Count);
        return uvs.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "funnel aggregation wrote {Rows} stat rows from {Instances} instances")]
    private static partial void LogAggregated(ILogger logger, int rows, int instances);

    private sealed class FunnelInstance(string identity, DateTime startUtc, AnalyticsClient client, string channel)
    {
        public string Identity { get; } = identity;

        public DateTime StartUtc { get; } = startUtc;

        public AnalyticsClient Client { get; } = client;

        public string Channel { get; } = channel;

        public HashSet<int> ReachedSteps { get; } = [];

        public int NextStep { get; private set; } = 1;

        public void Consume(int stepIndex)
        {
            ReachedSteps.Add(stepIndex);
            NextStep = stepIndex + 1;
        }
    }

    private sealed class FunnelAccumulator
    {
        public HashSet<string> View { get; } = [];

        public HashSet<string>[] Steps { get; } = [new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()];
    }
}
