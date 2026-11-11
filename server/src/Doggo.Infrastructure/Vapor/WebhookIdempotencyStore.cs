using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Doggo.Infrastructure.Vapor;

/// <summary>Lifecycle of one webhook key: in-flight processing or settled (replay-safe).</summary>
public enum WebhookClaimState
{
    /// <summary>The delivery is still being handled; a duplicate must not be acknowledged yet.</summary>
    Processing,

    /// <summary>The delivery finished; a duplicate is a true replay and safe to acknowledge.</summary>
    Completed
}

/// <summary>
/// Result of a claim attempt. <see cref="Claimed"/> is true only for the caller that won the
/// key. When false, <see cref="State"/> distinguishes an in-flight duplicate (<see cref="WebhookClaimState.Processing"/>,
/// retryable) from a settled one (<see cref="WebhookClaimState.Completed"/>, a genuine replay).
/// </summary>
public readonly record struct WebhookClaimResult(bool Claimed, WebhookClaimState State)
{
    public static readonly WebhookClaimResult ClaimedProcessing = new(true, WebhookClaimState.Processing);
    public static readonly WebhookClaimResult ReplayCompleted = new(false, WebhookClaimState.Completed);
    public static readonly WebhookClaimResult InFlight = new(false, WebhookClaimState.Processing);
}

/// <summary>
/// Deduplicates webhook deliveries. <see cref="TryClaimAsync"/> returns
/// <see cref="WebhookClaimResult.ClaimedProcessing"/> exactly once per key, so replays of the
/// same event are dropped. A claim lives in the Processing state for <c>processingTtl</c>; a
/// successful handler advances it to Completed (surviving for <c>completedTtl</c>), while a
/// failed handler releases it so the delivery can be retried. A Processing key that outlives
/// its TTL (crashed handler, or a release that could not reach Redis) self-expires so a later
/// retry reprocesses instead of being misjudged as a settled replay.
/// </summary>
public interface IWebhookIdempotencyStore
{
    /// <summary>
    /// Claims a key for one processing attempt. A fresh key starts in the Processing state and
    /// expires after <paramref name="processingTtl"/>; a completed key survives for
    /// <paramref name="completedTtl"/>. Returns Claimed=true only to the winning caller.
    /// </summary>
    Task<WebhookClaimResult> TryClaimAsync(string key, TimeSpan processingTtl, TimeSpan completedTtl, CancellationToken cancellationToken = default);

    /// <summary>Marks a claimed key as processed so later replays are acknowledged as duplicates.</summary>
    Task CompleteAsync(string key, TimeSpan completedTtl, CancellationToken cancellationToken = default);

    /// <summary>Releases a claim so a failed delivery can be retried instead of treated as a replay.</summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Process-local fallback; used when Redis is not reachable. Scoped to one instance.</summary>
public sealed class InMemoryWebhookIdempotencyStore : IWebhookIdempotencyStore
{
    private readonly ConcurrentDictionary<string, (WebhookClaimState State, DateTimeOffset ExpiresAt)> _claims = new();

    public Task<WebhookClaimResult> TryClaimAsync(string key, TimeSpan processingTtl, TimeSpan completedTtl, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        Prune(now);

        if (_claims.TryAdd(key, (WebhookClaimState.Processing, now + processingTtl)))
        {
            return Task.FromResult(WebhookClaimResult.ClaimedProcessing);
        }

        var existing = _claims[key];
        return Task.FromResult(
            existing.State == WebhookClaimState.Completed && existing.ExpiresAt > now
                ? WebhookClaimResult.ReplayCompleted
                : WebhookClaimResult.InFlight);
    }

    public Task CompleteAsync(string key, TimeSpan completedTtl, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_claims.TryGetValue(key, out var claim) && claim.State == WebhookClaimState.Processing)
        {
            _claims[key] = (WebhookClaimState.Completed, now + completedTtl);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        _claims.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var claim in _claims)
        {
            if (claim.Value.ExpiresAt <= now)
            {
                _claims.TryRemove(claim.Key, out _);
            }
        }
    }
}

/// <summary>
/// Redis-backed store (SET NX EX, two-phase Processing/Completed). Falls back to in-memory
/// claims while Redis is down so webhook handling keeps working; the tradeoff is possible
/// duplicate handling after a restart, which is acceptable because current handlers are
/// idempotent. The claim source (Redis vs. memory) is remembered per key so a release only
/// touches the store that actually held the claim — a memory-sourced claim never deletes a
/// Redis key that another instance may have claimed after Redis recovered.
/// </summary>
public sealed partial class RedisWebhookIdempotencyStore(
    IConnectionMultiplexer redis,
    ILogger<RedisWebhookIdempotencyStore> logger) : IWebhookIdempotencyStore
{
    private const string ProcessingValue = "processing";
    private const string CompletedValue = "completed";

    private readonly InMemoryWebhookIdempotencyStore _fallback = new();
    private readonly ConcurrentDictionary<string, bool> _claimedInRedis = new();

    public async Task<WebhookClaimResult> TryClaimAsync(string key, TimeSpan processingTtl, TimeSpan completedTtl, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = redis.GetDatabase();
            if (await database.StringSetAsync(key, ProcessingValue, processingTtl, When.NotExists).ConfigureAwait(false))
            {
                _claimedInRedis[key] = true;
                return WebhookClaimResult.ClaimedProcessing;
            }

            var value = await database.StringGetAsync(key).ConfigureAwait(false);
            return value == CompletedValue ? WebhookClaimResult.ReplayCompleted : WebhookClaimResult.InFlight;
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            LogFallback(ex.Message);
            var result = await _fallback.TryClaimAsync(key, processingTtl, completedTtl, cancellationToken).ConfigureAwait(false);
            if (result.Claimed)
            {
                _claimedInRedis[key] = false;
            }

            return result;
        }
    }

    public async Task CompleteAsync(string key, TimeSpan completedTtl, CancellationToken cancellationToken = default)
    {
        if (_claimedInRedis.TryGetValue(key, out var inRedis) && !inRedis)
        {
            _claimedInRedis.TryRemove(key, out _);
            await _fallback.CompleteAsync(key, completedTtl, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Best effort: a completed key that cannot be written keeps the short Processing TTL,
        // so a replay gets a retryable signal until it self-expires and reprocesses idempotently.
        try
        {
            var database = redis.GetDatabase();
            await database.StringSetAsync(key, CompletedValue, completedTtl, When.Exists).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            LogFallback(ex.Message);
        }

        _claimedInRedis.TryRemove(key, out _);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_claimedInRedis.TryGetValue(key, out var inRedis) && !inRedis)
        {
            // The claim was held in memory only: never touch Redis, another instance may own it.
            _claimedInRedis.TryRemove(key, out _);
            await _fallback.ReleaseAsync(key, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            await database.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            // The Redis key survives in the Processing state; its short TTL lets a later retry
            // reprocess instead of being misjudged as a settled replay.
            LogFallback(ex.Message);
        }

        _claimedInRedis.TryRemove(key, out _);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "redis unavailable for webhook idempotency, using in-memory fallback: {Message}")]
    private partial void LogFallback(string message);
}
