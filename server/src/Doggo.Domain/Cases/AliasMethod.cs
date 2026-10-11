namespace Doggo.Domain.Cases;

/// <summary>
/// Alias method (Vose) for weighted sampling: O(n) setup, O(1) per draw, no prefix sums.
/// Built from integer weights; sampling takes a uniform double in [0,1) and maps it to a
/// bucket index, so tests can drive exact outcomes without mocking Random.
/// </summary>
public sealed class AliasSampler
{
    private readonly double[] _probabilities;
    private readonly int[] _aliases;

    public AliasSampler(IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            throw new ArgumentException("At least one weight is required.", nameof(weights));
        }

        if (weights.Any(w => w < 0))
        {
            throw new ArgumentException("Weights must not be negative.", nameof(weights));
        }

        var total = weights.Sum();
        if (total == 0)
        {
            throw new ArgumentException("At least one weight must be positive.", nameof(weights));
        }

        var n = weights.Count;
        _probabilities = new double[n];
        _aliases = new int[n];

        // Scale every weight to the average so small and large counts share one cutoff.
        var scaled = weights.Select(w => w * n / (double)total).ToArray();
        var small = new Queue<int>(Enumerable.Range(0, n).Where(i => scaled[i] < 1.0));
        var large = new Queue<int>(Enumerable.Range(0, n).Where(i => scaled[i] >= 1.0));

        while (small.Count > 0 && large.Count > 0)
        {
            var s = small.Dequeue();
            var l = large.Dequeue();

            _probabilities[s] = scaled[s];
            _aliases[s] = l;

            // The large bucket gives the remainder to the small one; it may itself drop below 1.
            scaled[l] = scaled[l] + scaled[s] - 1.0;
            if (scaled[l] < 1.0)
            {
                small.Enqueue(l);
            }
            else
            {
                large.Enqueue(l);
            }
        }

        // Buckets left over are exact (probability 1 or 0 up to rounding).
        while (large.Count > 0)
        {
            var l = large.Dequeue();
            _probabilities[l] = 1.0;
            _aliases[l] = l;
        }

        while (small.Count > 0)
        {
            var s = small.Dequeue();
            _probabilities[s] = 1.0;
            _aliases[s] = s;
        }
    }

    public AliasSampler(IReadOnlyList<int> weights)
        : this(weights.Select(w => (long)w).ToList())
    {
    }

    public int BucketCount => _probabilities.Length;

    /// <summary>Maps a uniform sample in [0,1) onto the weighted buckets.</summary>
    public int Sample(double uniform)
    {
        if (uniform is < 0.0 or >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(uniform), uniform, "Uniform sample must be in [0,1).");
        }

        var scaled = uniform * _probabilities.Length;
        var bucket = (int)scaled;
        var within = scaled - bucket;
        return within < _probabilities[bucket] ? bucket : _aliases[bucket];
    }

    public int Sample(Random random) => Sample(random.NextDouble());
}
