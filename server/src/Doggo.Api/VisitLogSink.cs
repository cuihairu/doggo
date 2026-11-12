using System.Threading.Channels;
using Doggo.Domain.Analytics;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api;

/// <summary>One visit to log; Date is the UTC day it happened on.</summary>
public sealed record VisitLogEntry(DateOnly Date, Guid? UserId, string VisitorKey, string Path, AnalyticsClient Client);

/// <summary>
/// Bounded fire-and-forget queue between the visit log middleware and the batch writer: the
/// request path only enqueues and never touches the database, so a slow or unavailable store
/// cannot delay a response. When the queue is full, the newest analytics row is dropped and
/// counted (the row is expendable; the response must not suffer for it).
/// </summary>
public sealed class VisitLogSink
{
    private readonly Channel<VisitLogEntry> _queue = Channel.CreateBounded<VisitLogEntry>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropWrite });
    private long _dropped;

    public ChannelReader<VisitLogEntry> Reader => _queue.Reader;

    public void TryAdd(VisitLogEntry entry)
    {
        if (!_queue.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);
}

/// <summary>
/// Drains the visit log queue in batches and writes them with one SaveChanges per flush, so
/// a busy site does not write a row per request. Failures are logged and dropped — a missed
/// analytics row is preferable to an unbounded retry loop.
/// </summary>
public sealed partial class VisitLogBatchWorker(
    VisitLogSink sink,
    IServiceScopeFactory scopeFactory,
    ILogger<VisitLogBatchWorker> logger) : BackgroundService
{
    private const int BatchSize = 200;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<VisitLogEntry>(BatchSize);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for the first entry, then drain the rest within the delay window so a
                // trickle of visits still flushes promptly.
                var first = await sink.Reader.ReadAsync(stoppingToken);
                pending.Add(first);
                var deadline = DateTime.UtcNow + MaxDelay;
                while (pending.Count < BatchSize
                       && DateTime.UtcNow < deadline
                       && sink.Reader.TryRead(out var next))
                {
                    pending.Add(next);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushAsync(pending, stoppingToken);
            pending.Clear();
        }
    }

    private async Task FlushAsync(IReadOnlyList<VisitLogEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            foreach (var entry in entries)
            {
                db.UserVisitLogs.Add(new UserVisitLog
                {
                    Id = Guid.CreateVersion7(),
                    Date = entry.Date,
                    UserId = entry.UserId,
                    VisitorKey = entry.VisitorKey,
                    Path = entry.Path,
                    Client = entry.Client
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogFlushFailed(entries.Count, ex.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "visit log flush of {Count} rows failed: {Reason}")]
    private partial void LogFlushFailed(int count, string reason);
}
