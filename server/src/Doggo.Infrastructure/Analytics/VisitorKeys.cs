using System.Security.Cryptography;
using System.Text;

namespace Doggo.Infrastructure.Analytics;

/// <summary>
/// Stable per (ip, user agent) key, hashed so raw addresses are not stored. Shared by
/// VisitLogMiddleware and the analytics recorder so both tables stay in one identity space.
/// </summary>
public static class VisitorKeys
{
    public static string Compute(string? ip, string? userAgent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ip ?? string.Empty}|{userAgent ?? string.Empty}")))[..32];
}
