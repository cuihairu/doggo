using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Auth;

public sealed record SteamBindResult(Guid UserId, string SteamId);

/// <summary>
/// Steam OpenID 2.0 login: builds the checkid_setup URL and verifies the redirect response
/// server-to-server (check_authentication against steamcommunity.com). The state parameter
/// carries an HMAC of (userId, expiry) so a callback can only complete for the user that
/// started the bind.
/// </summary>
public sealed partial class SteamOpenIdService(IOptions<AuthOptions> optionsFactory, HttpClient http, ILogger<SteamOpenIdService> logger)
{
    public const string Endpoint = "https://steamcommunity.com/openid/login";

    private readonly AuthOptions _options = optionsFactory.Value;

    /// <summary>Starts the bind: the redirect target for the user's browser.</summary>
    public string BuildLoginUrl(Guid userId, string publicBaseUrl)
    {
        var returnUrl = $"{publicBaseUrl.TrimEnd('/')}/api/auth/steam/callback";
        var state = Protect(userId);

        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnUrl,
            ["openid.realm"] = publicBaseUrl.TrimEnd('/'),
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.state"] = state
        };

        return $"{Endpoint}?{string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"))}";
    }

    /// <summary>
    /// Verifies the OpenID redirect: asks Steam to confirm the assertion, then returns the
    /// bound user and steamid. Returns null when Steam rejects the assertion or the state
    /// does not match (wrong HMAC or expired).
    /// </summary>
    public async Task<SteamBindResult?> VerifyCallbackAsync(IEnumerable<KeyValuePair<string, string?>> query, CancellationToken cancellationToken = default)
    {
        var values = query
            .Where(kv => kv.Key.StartsWith("openid.", StringComparison.Ordinal) && kv.Value is not null)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!))
            .ToList();

        var state = values.FirstOrDefault(kv => kv.Key == "openid.state").Value;
        if (state is null || Unprotect(state) is not { } userId)
        {
            LogBadState();
            return null;
        }

        var claimedId = values.FirstOrDefault(kv => kv.Key == "openid.claimed_id").Value;
        var steamId = ExtractSteamId(claimedId);
        if (steamId is null)
        {
            LogNoSteamId();
            return null;
        }

        var form = new Dictionary<string, string>(values)
        {
            ["openid.mode"] = "check_authentication"
        };

        using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!body.Contains("is_valid:true", StringComparison.Ordinal))
        {
            LogRejected(steamId);
            return null;
        }

        return new SteamBindResult(userId, steamId);
    }

    /// <summary>steamid is the 17-digit number at the end of the claimed identifier.</summary>
    public static string? ExtractSteamId(string? claimedId)
    {
        if (string.IsNullOrEmpty(claimedId))
        {
            return null;
        }

        const string prefix = "https://steamcommunity.com/openid/id/";
        if (!claimedId.StartsWith(prefix, StringComparison.Ordinal) || claimedId.Length <= prefix.Length)
        {
            return null;
        }

        var steamId = claimedId[prefix.Length..];
        return steamId.All(char.IsDigit) && steamId.Length == 17 ? steamId : null;
    }

    private string Protect(Guid userId)
    {
        var payload = $"{userId:N}:{DateTime.UtcNow.AddMinutes(15):O}";
        var data = Encoding.UTF8.GetBytes(payload);
        var tag = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningKey), data);
        return Convert.ToBase64String(data) + "." + Convert.ToBase64String(tag);
    }

    private Guid? Unprotect(string state)
    {
        try
        {
            var parts = state.Split('.', 2);
            if (parts.Length != 2)
            {
                return null;
            }

            var data = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);
            var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningKey), data);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return null;
            }

            var payload = Encoding.UTF8.GetString(data).Split(':', 2);
            if (payload.Length != 2 || DateTime.Parse(payload[1]) < DateTime.UtcNow)
            {
                return null;
            }

            return Guid.ParseExact(payload[0], "N");
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string SigningKey => _options.HasSigningKey
        ? _options.SecretKey
        : "doggo-steam-bind-fallback-key";

    [LoggerMessage(Level = LogLevel.Warning, Message = "steam bind callback came with a missing or invalid state")]
    private partial void LogBadState();

    [LoggerMessage(Level = LogLevel.Warning, Message = "steam bind callback had no usable claimed id")]
    private partial void LogNoSteamId();

    [LoggerMessage(Level = LogLevel.Warning, Message = "steam rejected the assertion for {SteamId}")]
    private partial void LogRejected(string steamId);
}
