using System.Globalization;
using System.Text;

namespace Doggo.PriceCrawler;

/// <summary>
/// Parses price text like "¥1,284.50" or "1284" into minor units (cents).
/// The dot is treated as the decimal separator and commas/spaces as group separators;
/// currency symbols and surrounding words are ignored. Text with a minus sign is rejected
/// (prices are non-negative; "-50%" notation would corrupt the value). Ambiguous locales
/// are out of scope — adapters that need them parse before handing text over.
/// </summary>
public static class PriceTextParser
{
    public static bool TryParse(string? text, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Contains('-') || text.Contains('−'))
        {
            return false;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch) || ch == '.' || ch == ',')
            {
                sb.Append(ch);
            }
        }

        var digits = sb.ToString().Replace(",", string.Empty).TrimEnd('.');
        if (digits.Length == 0 || !decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (value <= 0)
        {
            return false;
        }

        cents = (long)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
        return cents > 0;
    }
}
