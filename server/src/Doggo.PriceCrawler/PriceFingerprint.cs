using System.Security.Cryptography;
using System.Text;

namespace Doggo.PriceCrawler;

/// <summary>
/// Page fingerprinting for the raw capture table: SHA-256 over the normalized page text,
/// so re-parsing the same content is recognizable (pattern follows NewsFingerprint).
/// </summary>
public static class PriceFingerprint
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\0", string.Empty).Trim().ToLowerInvariant();
    }

    public static string Compute(string? text) => ComputeHash(Normalize(text));

    public static string ComputeHash(string normalized)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
