using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Analytics;

/// <summary>
/// Deletes analytics event rows once they are outside the aggregation window (funnel design §10 #9).
/// FunnelDailyStat rows are kept permanently: the funnel page reads only the daily stats.
/// </summary>
public sealed class AnalyticsRetention(DoggoDbContext db, ILogger<AnalyticsRetention> logger)
{
    private const int PurgeBatchSize = 500;

    /// <summary>
    /// Batched delete (the InMemory provider has no ExecuteDelete, and the loop bounds memory
    /// on a large table). Rows are dropped only when they are old by both measures: OccurredAtUtc
    /// past the cutoff, so the 7-day rolling recompute can no longer reach them, and ReceivedAtUtc
    /// past it too, so a late-arriving event is never removed before it has been aggregated.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        while (true)
        {
            var batch = await db.AnalyticsEvents
                .Where(e => e.OccurredAtUtc < cutoffUtc && e.ReceivedAtUtc < cutoffUtc)
                .OrderBy(e => e.OccurredAtUtc)
                .Take(PurgeBatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            db.AnalyticsEvents.RemoveRange(batch);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            deleted += batch.Count;
            if (batch.Count < PurgeBatchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("Purged {Count} analytics events older than {Cutoff:yyyy-MM-dd}", deleted, cutoffUtc);
        }

        return deleted;
    }
}
