using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Doggo.Infrastructure.Vapor;

/// <summary>
/// Event pushed by the vapor control plane (WebhookDispatcher). Fields mirror the
/// envelope produced by vapor; <see cref="Payload"/> keeps the raw event body so
/// handlers can parse per-type shapes.
/// </summary>
public sealed record VaporWebhookEnvelope(
    Guid Id,
    string Category,
    string Type,
    Guid? JobId,
    string? AccountName,
    string? State,
    string? Message,
    DateTimeOffset Timestamp,
    JsonElement? Payload);

public static class VaporWebhook
{
    public const string TimestampHeader = "X-Vapor-Timestamp";
    public const string SignatureHeader = "X-Vapor-Signature";
    private const string SignaturePrefix = "sha256=";

    /// <summary>
    /// Verifies a webhook request: vapor signs "{unixSeconds}.{body}" with HMAC-SHA256 and
    /// sends the digest as "sha256=&lt;hex&gt;". Rejects missing headers, malformed input,
    /// stale timestamps and forged signatures, all without leaking timing information.
    /// </summary>
    public static bool VerifySignature(
        string body,
        string? timestampHeader,
        string? signatureHeader,
        string secret,
        int timestampToleranceSeconds,
        DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(secret)
            || string.IsNullOrEmpty(body)
            || string.IsNullOrEmpty(timestampHeader)
            || string.IsNullOrEmpty(signatureHeader)
            || !signatureHeader.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var providedHex = signatureHeader[SignaturePrefix.Length..];
        if (providedHex.Length != 64 || !IsLowerHex(providedHex))
        {
            return false;
        }

        if (!long.TryParse(timestampHeader, out var unixSeconds) || unixSeconds <= 0)
        {
            return false;
        }

        var ageSeconds = Math.Abs(now.ToUnixTimeSeconds() - unixSeconds);
        if (ageSeconds > timestampToleranceSeconds)
        {
            return false;
        }

        var expected = ComputeSignature(secret, timestampHeader, body);
        var provided = Convert.FromHexString(providedHex);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>Computes the exact bytes vapor signs, also used by tests to build valid requests.</summary>
    public static byte[] ComputeSignature(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
    }

    private static bool IsLowerHex(string value) =>
        value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
