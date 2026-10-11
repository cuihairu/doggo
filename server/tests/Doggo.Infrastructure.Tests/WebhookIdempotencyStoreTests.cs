using Doggo.Infrastructure.Vapor;

namespace Doggo.Infrastructure.Tests;

public class WebhookIdempotencyStoreTests
{
    [Fact]
    public async Task Claims_a_key_only_once()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        Assert.True(await store.TryClaimAsync("vapor:webhook:a", TimeSpan.FromMinutes(1)));
        Assert.False(await store.TryClaimAsync("vapor:webhook:a", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Different_keys_claim_independently()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        Assert.True(await store.TryClaimAsync("vapor:webhook:a", TimeSpan.FromMinutes(1)));
        Assert.True(await store.TryClaimAsync("vapor:webhook:b", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Expired_claims_are_reclaimable()
    {
        var store = new InMemoryWebhookIdempotencyStore();

        Assert.True(await store.TryClaimAsync("vapor:webhook:a", TimeSpan.Zero));
        Assert.True(await store.TryClaimAsync("vapor:webhook:a", TimeSpan.FromMinutes(1)));
    }
}
