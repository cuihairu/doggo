using Doggo.Domain.Cases;

namespace Doggo.Domain.Tests;

public class AliasMethodTests
{
    [Fact]
    public void Sample_EqualWeights_HitsBucketsUniformly()
    {
        var sampler = new AliasSampler([1, 1, 1, 1]);
        var random = new Random(20261011);
        var counts = new int[sampler.BucketCount];
        const int draws = 100_000;

        for (var i = 0; i < draws; i++)
        {
            counts[sampler.Sample(random)]++;
        }

        foreach (var count in counts)
        {
            Assert.InRange(count, draws / 4 - 1_500, draws / 4 + 1_500);
        }
    }

    [Fact]
    public void Sample_SkewedWeights_MatchesWeightRatios()
    {
        // 70 / 20 / 10 split over 100k draws.
        var sampler = new AliasSampler([70L, 20L, 10L]);
        var random = new Random(42);
        var counts = new int[sampler.BucketCount];
        const int draws = 100_000;

        for (var i = 0; i < draws; i++)
        {
            counts[sampler.Sample(random)]++;
        }

        Assert.InRange(counts[0], 68_000, 72_000);
        Assert.InRange(counts[1], 18_500, 21_500);
        Assert.InRange(counts[2], 8_500, 11_500);
    }

    [Fact]
    public void Sample_SingleWeight_AlwaysReturnsZero()
    {
        var sampler = new AliasSampler([5]);
        var random = new Random(7);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, sampler.Sample(random));
        }
    }

    [Fact]
    public void Sample_UniformDrivesExactOutcome()
    {
        // uniform < 0.5 lands on bucket 0 itself; uniform in [0.5,1) falls in bucket 1
        // whose own weight 0 forces the alias to bucket 0.
        var sampler = new AliasSampler([1L, 0L]);

        Assert.Equal(0, sampler.Sample(0.25));
        Assert.Equal(0, sampler.Sample(0.75));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.0)]
    [InlineData(2.5)]
    public void Sample_OutsideUnitInterval_Throws(double uniform)
    {
        var sampler = new AliasSampler([1, 1]);

        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.Sample(uniform));
    }

    [Fact]
    public void Constructor_EmptyWeights_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AliasSampler(Array.Empty<long>()));
    }

    [Fact]
    public void Constructor_NegativeWeight_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AliasSampler([1, -1]));
    }

    [Fact]
    public void Constructor_AllZeroWeights_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AliasSampler([0L, 0L, 0L]));
    }

    [Fact]
    public void Sample_IntWeightsOverload_MatchesLongOverload()
    {
        var fromInts = new AliasSampler([3, 1]);
        var fromLongs = new AliasSampler([3L, 1L]);

        for (var i = 1; i < 100; i++)
        {
            var uniform = i / 128.0;
            Assert.Equal(fromLongs.Sample(uniform), fromInts.Sample(uniform));
        }
    }
}
