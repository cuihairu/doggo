using System.Globalization;

namespace Doggo.PriceCrawler;

/// <summary>
/// Cleaning rules from docs/design/price-crawler-design.md §6: validity window, ISO-4217
/// normalization, dedup window key, rolling-median suspect flag, CNY conversion.
/// </summary>
public static class PriceCleaning
{
    /// <summary>Currency codes we can convert (must match ExchangeRate rows maintained elsewhere).</summary>
    public static readonly IReadOnlyList<string> KnownCurrencyCodes =
    [
        "CNY", "USD", "EUR", "GBP", "JPY", "KRW", "RUB", "HKD", "TWD", "CAD", "AUD", "BRL", "TRY", "PLN", "INR",
    ];

    /// <summary>0 &lt; price ≤ max (design §6: reject negatives, zero and absurd values).</summary>
    public static bool IsPriceValid(long cents, long maxCents) => cents > 0 && cents <= maxCents;

    /// <summary>Uppercase + membership check; unknown codes are rejected so bad rows never convert.</summary>
    public static bool TryNormalizeCurrency(string? code, out string iso)
    {
        iso = (code ?? string.Empty).Trim().ToUpperInvariant();
        return iso.Length == 3 && iso.All(char.IsAsciiLetter) && KnownCurrencyCodes.Contains(iso);
    }

    /// <summary>Dedup window key "yyyyMMddHHmm" floored to the window size (default 60 min).</summary>
    public static string WindowKey(DateTime observedUtc, int windowMinutes)
    {
        var minutes = Math.Max(windowMinutes, 1);
        var utc = observedUtc.Kind == DateTimeKind.Utc ? observedUtc : observedUtc.ToUniversalTime();
        var floored = utc.AddMinutes(-(utc.Minute % minutes));
        return floored.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// True when the point deviates more than <paramref name="thresholdPct"/> from the median
    /// of the previous <paramref name="recentCents"/> points (design §6). Empty history → not suspect.
    /// </summary>
    public static bool IsSuspect(long cents, IReadOnlyList<long> recentCents, decimal thresholdPct)
    {
        if (recentCents.Count == 0 || thresholdPct <= 0)
        {
            return false;
        }

        var median = Median(recentCents);
        if (median <= 0)
        {
            return false;
        }

        var deviation = Math.Abs((decimal)cents - median) * 100m / median;
        return deviation > thresholdPct;
    }

    /// <summary>ConvertedCents = round(price × rate); null rate propagates (keep raw currency).</summary>
    public static long? ConvertCents(long cents, decimal? rate) =>
        rate is null ? null : (long)Math.Round(cents * rate.Value, MidpointRounding.AwayFromZero);

    public static long Median(IReadOnlyList<long> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
