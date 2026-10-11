using System.Security.Cryptography;
using System.Text;

namespace Doggo.Scraper.News;

/// <summary>
/// URL canonicalization and fingerprinting. The fingerprint is the dedup key stored on
/// <see cref="Doggo.Domain.News.NewsItem.UrlHash"/>; tracking parameters and fragments are
/// ignored so the same article reached via different share links is stored once.
/// </summary>
public static class NewsFingerprint
{
    private static readonly HashSet<string> IgnoredQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "spm", "from", "share_from", "share_id", "isappinstalled", "igshid",
        "fbclid", "gclid", "ref", "ref_src", "yclid", "mc_cid", "mc_eid",
    };

    public static string CanonicalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme.ToLowerInvariant()).Append("://").Append(uri.Host.ToLowerInvariant());
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        builder.Append(path.Length == 0 ? "/" : path);

        var kept = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 0)
            .Select(p => p.Split('=', 2))
            .Where(parts => parts.Length == 2 && !IgnoredQueryNames.Contains(parts[0]))
            .Select(parts => $"{parts[0]}={parts[1]}")
            .OrderBy(p => p, StringComparer.Ordinal);

        var query = string.Join('&', kept);
        if (query.Length > 0)
        {
            builder.Append('?').Append(query);
        }

        return builder.ToString();
    }

    public static string ComputeUrlHash(string url)
    {
        var canonical = CanonicalizeUrl(url);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
