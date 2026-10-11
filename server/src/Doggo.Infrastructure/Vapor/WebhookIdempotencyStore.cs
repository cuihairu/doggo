using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Doggo.Infrastructure.Vapor;

/// <summary>
/// Deduplicates webhook deliveries. <see cref="TryClaimAsync"/> returns true exactly once per key
/// within the TTL, so replays of the same event are dropped.
/// </summary>
public interface IWebhookIdempotencyStore
{
    /// <summary>Returns true when the key was claimed by this call, false when it was already seen.</summary>
    Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default);
}

/// <summary>Process-local fallback; used when Redis is not reachable.</summary>
public sealed class InMemoryWebhookIdempotencyStore : IWebhookIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _claims = new();

    public Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var claim in _claims)
        {
            if (claim.Value <= now)
            {
                _claims.TryRemove(claim.Key, out _);
            }
        }

        return Task.FromResult(_claims.TryAdd(key, now + ttl));
    }
}

/// <summary>
/// Redis-backed store (SET NX EX). Falls back to in-memory claims while Redis is down so
/// webhook handling keeps working; the tradeoff is possible duplicate handling after a
/// restart, which is acceptable because current handlers are idempotent.
/// </summary>
public sealed partial class RedisWebhookIdempotencyStore(
    IConnectionMultiplexer redis,
    ILogger<RedisWebhookIdempotencyStore> logger) : IWebhookIdempotencyStore
{
    private readonly InMemoryWebhookIdempotencyStore _fallback = new();

    public async Task<bool> TryClaimAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = redis.GetDatabase();
            return await database.StringSetAsync(key, "1", ttl, When.NotExists).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            LogFallback(ex.Message);
            return await _fallback.TryClaimAsync(key, ttl, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "redis unavailable for webhook idempotency, using in-memory fallback: {Message}")]
    private partial void LogFallback(string message);
}
