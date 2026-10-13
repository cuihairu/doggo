using Doggo.PriceCrawler;

namespace Doggo.PriceCrawler.Tests;

public class PriceCrawlRetryTests
{
    private static PriceCrawlerRetryOptions Options() => new()
    {
        MaxAttempts = 3,
        BaseDelaySeconds = 1,
        Factor = 2,
        CapSeconds = 60,
        JitterRatio = 0
    };

    [Fact]
    public async Task RetriesUntilSuccess()
    {
        var attempts = 0;
        var result = await PriceCrawlRetry.ExecuteAsync(Options(), (_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts < 3 ? throw new HttpRequestException("boom") : 42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ThrowsAfterMaxAttempts()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            PriceCrawlRetry.ExecuteAsync<int>(Options(), (_, _) =>
            {
                attempts++;
                throw new HttpRequestException("boom");
            }, CancellationToken.None));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task DoesNotRetryCancellation()
    {
        var attempts = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            PriceCrawlRetry.ExecuteAsync<int>(Options(), (_, _) =>
            {
                attempts++;
                throw new OperationCanceledException();
            }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void BackoffGrowsAndCaps()
    {
        var options = Options();
        Assert.Equal(1, PriceCrawlRetry.Delay(options, 1));
        Assert.Equal(2, PriceCrawlRetry.Delay(options, 2));
        Assert.Equal(4, PriceCrawlRetry.Delay(options, 3));
        Assert.Equal(60, PriceCrawlRetry.Delay(options, 20)); // capped
    }

    [Fact]
    public void JitterStaysWithinRatio()
    {
        var options = new PriceCrawlerRetryOptions { BaseDelaySeconds = 10, Factor = 1, CapSeconds = 600, JitterRatio = 0.25 };
        foreach (var attempt in Enumerable.Range(1, 50))
        {
            var delay = PriceCrawlRetry.Delay(options, attempt);
            Assert.InRange(delay, 7, 13);
        }
    }
}
