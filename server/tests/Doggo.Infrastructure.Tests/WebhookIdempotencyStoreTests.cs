using Doggo.Infrastructure.Vapor;

namespace Doggo.Infrastructure.Tests;

public class WebhookIdempotencyStoreTests
{
    private static readonly TimeSpan Processing = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Completed = TimeSpan.FromDays(7);

    [Fact]
    public async Task Claims_a_key_only_once()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        var first = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.True(first.Claimed);
        Assert.Equal(WebhookClaimState.Processing, first.State);

        var second = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.False(second.Claimed);
    }

    [Fact]
    public async Task Different_keys_claim_independently()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        Assert.True((await store.TryClaimAsync("vapor:webhook:a", Processing, Completed)).Claimed);
        Assert.True((await store.TryClaimAsync("vapor:webhook:b", Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Expired_claims_are_reclaimable()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        Assert.True((await store.TryClaimAsync("vapor:webhook:a", TimeSpan.Zero, Completed)).Claimed);
        Assert.True((await store.TryClaimAsync("vapor:webhook:a", Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Released_claim_can_be_reclaimed()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        var claim = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.True(claim.Claimed);
        await store.ReleaseAsync("vapor:webhook:a");
        Assert.True((await store.TryClaimAsync("vapor:webhook:a", Processing, Completed)).Claimed);
    }

    [Fact]
    public async Task Completed_claim_is_reported_as_a_replay()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        var claim = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        await store.CompleteAsync("vapor:webhook:a", Completed);

        var replay = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.False(replay.Claimed);
        Assert.Equal(WebhookClaimState.Completed, replay.State);
    }

    [Fact]
    public async Task In_flight_duplicate_is_reported_as_processing()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);

        var duplicate = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.False(duplicate.Claimed);
        Assert.Equal(WebhookClaimState.Processing, duplicate.State);
    }

    [Fact]
    public async Task Processing_claim_expires_and_is_reclaimable()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        // A zero processing TTL expires immediately, so a later claim reclaims it. The key is
        // treated as in-flight (not completed), which is what lets a stuck delivery be retried.
        await store.TryClaimAsync("vapor:webhook:a", TimeSpan.Zero, Completed);
        var reclaim = await store.TryClaimAsync("vapor:webhook:a", Processing, Completed);
        Assert.True(reclaim.Claimed);
        Assert.Equal(WebhookClaimState.Processing, reclaim.State);
    }
}
