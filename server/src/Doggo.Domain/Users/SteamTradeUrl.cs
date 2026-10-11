using System.Globalization;
using System.Text.RegularExpressions;

namespace Doggo.Domain.Users;

public sealed record TradeUrlParts(string SteamId, string Partner, string Token);

/// <summary>
/// Parses Steam trade offer URLs. The partner parameter is the 32-bit account id; the
/// SteamID64 form used across the codebase is 76561197960265728 + account id.
/// </summary>
public static class SteamTradeUrl
{
    private const ulong SteamId64Offset = 76561197960265728UL;

    private static readonly Regex UrlPattern = new(
        @"^https?://steamcommunity\.com/tradeoffer/new/\?([^#\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? url, out TradeUrlParts parts)
    {
        parts = new TradeUrlParts(string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var match = UrlPattern.Match(url.Trim());
        if (!match.Success)
        {
            return false;
        }

        string? partner = null;
        string? token = null;

        foreach (var pair in match.Groups[1].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2)
            {
                continue;
            }

            if (split[0].Equals("partner", StringComparison.OrdinalIgnoreCase))
            {
                partner = Uri.UnescapeDataString(split[1].Trim());
            }
            else if (split[0].Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                token = Uri.UnescapeDataString(split[1].Trim());
            }
        }

        if (string.IsNullOrEmpty(partner) || string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!ulong.TryParse(partner, out var accountId) || accountId == 0)
        {
            return false;
        }

        if (!Regex.IsMatch(token, "^[A-Za-z0-9_-]{6,}$"))
        {
            return false;
        }

        parts = new TradeUrlParts((SteamId64Offset + accountId).ToString(CultureInfo.InvariantCulture), partner, token);
        return true;
    }
}
