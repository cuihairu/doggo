using Doggo.Infrastructure.Vapor;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Doggo.Infrastructure.Tests;

/// <summary>
/// The Redis-backed store against a real Redis: two-phase claim/replay semantics, the
/// memory fallback while Redis is down (a disposed multiplexer makes every operation
/// throw RedisConnectionException), and the rule that a memory-sourced claim never
/// touches a Redis key another instance may own. Needs a Redis on 127.0.0.1:6379; CI
/// provides one as a service container.
/// </summary>
public class RedisWebhookIdempotencyStoreTests : IClassFixture<RedisFixture>
{
    private static readonly TimeSpan Processing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Completed = TimeSpan.FromDays(7);

    private readonly RedisFixture _redis;

    public RedisWebhookIdempotencyStoreTests(RedisFixture redis) => _redis = redis;

    private string NewKey() => $"doggo-tests:webhook:{Guid.NewGuid():N}";

    [Fact]
    public async Task Claim_wins_once_then_reports_in_flight_duplicates()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        var first = await store.TryClaimAsync(key, Processing, Completed);
        Assert.True(first.Claimed);
        Assert.Equal(WebhookClaimState.Processing, first.State);

        var second = await new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance)
            .TryClaimAsync(key, Processing, Completed);
        Assert.False(second.Claimed);
        Assert.Equal(WebhookClaimState.Processing, second.State);
    }

    [Fact]
    public async Task Completed_key_is_reported_as_a_replay()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        Assert.True((await store.TryClaimAsync(key, Processing, Completed)).Claimed);
        await store.CompleteAsync(key, Completed);

        var replay = await new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance)
            .TryClaimAsync(key, Processing, Completed);
        Assert.False(replay.Claimed);
        Assert.Equal(WebhookClaimState.Completed, replay.State);
    }

    [Fact]
    public async Task Released_key_is_reclaimable_from_redis()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        Assert.True((await store.TryClaimAsync(key, Processing, Completed)).Claimed);
        await store.ReleaseAsync(key);

        var db = _redis.Multiplexer.GetDatabase();
        Assert.False(db.KeyExists(key));
        Assert.True((await store.TryClaimAsync(key, Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Redis_outage_claims_in_memory_and_the_release_stays_off_redis()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        // A disposed multiplexer makes every Redis operation throw RedisConnectionException,
        // which is how an outage reaches the store's fallback path.
        await _redis.Multiplexer.CloseAsync();

        var claim = await store.TryClaimAsync(key, Processing, Completed);
        Assert.True(claim.Claimed);

        await _redis.ReconnectAsync();

        // The claim was memory-sourced, so releasing it must not touch Redis even though
        // Redis is back: another instance may have claimed the key in the meantime.
        await store.ReleaseAsync(key);
        var db = _redis.Multiplexer.GetDatabase();
        Assert.False(db.KeyExists(key));
    }

    [Fact]
    public async Task Memory_sourced_release_leaves_another_instances_redis_claim_alone()
    {
        var key = NewKey();
        var outageStore = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        await _redis.Multiplexer.CloseAsync();
        Assert.True((await outageStore.TryClaimAsync(key, Processing, Completed)).Claimed);

        // Redis recovers and another instance claims the same delivery.
        await _redis.ReconnectAsync();
        var liveStore = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);
        Assert.True((await liveStore.TryClaimAsync(key, Processing, Completed)).Claimed);

        // The first instance's memory-sourced release must not delete the other claim.
        await outageStore.ReleaseAsync(key);

        var db = _redis.Multiplexer.GetDatabase();
        Assert.True(db.KeyExists(key));
        Assert.Equal("processing", db.StringGet(key));
        Assert.False((await liveStore.TryClaimAsync(key, Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Memory_sourced_complete_lands_in_memory_only()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);

        await _redis.Multiplexer.CloseAsync();
        Assert.True((await store.TryClaimAsync(key, Processing, Completed)).Claimed);

        await _redis.ReconnectAsync();
        await store.CompleteAsync(key, Completed);

        var db = _redis.Multiplexer.GetDatabase();
        Assert.False(db.KeyExists(key));
        Assert.True((await new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance)
            .TryClaimAsync(key, Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Redis_outage_during_complete_is_swallowed_and_the_key_stays_retryable()
    {
        var key = NewKey();
        var store = new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance);
        Assert.True((await store.TryClaimAsync(key, Processing, Completed)).Claimed);

        await _redis.Multiplexer.CloseAsync();
        await store.CompleteAsync(key, Completed);

        await _redis.ReconnectAsync();
        var db = _redis.Multiplexer.GetDatabase();
        Assert.Equal("processing", db.StringGet(key));
        Assert.False((await new RedisWebhookIdempotencyStore(_redis.Multiplexer, NullLogger<RedisWebhookIdempotencyStore>.Instance)
            .TryClaimAsync(key, Processing, Completed)).Claimed);
    }
}

/// <summary>Shared real-Redis connection for the Redis-backed idempotency tests.</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private ConnectionMultiplexer? _multiplexer;

    public IConnectionMultiplexer Multiplexer =>
        _multiplexer ?? throw new InvalidOperationException("redis fixture is not connected");

    public async Task InitializeAsync()
    {
        _multiplexer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,abortConnect=false");
    }

    public async Task DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }
    }

    /// <summary>Closes the multiplexer and reconnects, simulating outage then recovery.</summary>
    public async Task ReconnectAsync()
    {
        _multiplexer!.Dispose();
        _multiplexer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:6379,abortConnect=false");
    }
}
