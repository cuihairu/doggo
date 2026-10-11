namespace Doggo.Infrastructure.Auth;

/// <summary>JWT and refresh-token settings. SecretKey comes from configuration only
/// (Auth__SecretKey) and must never be committed; without it the Api falls back to an
/// ephemeral per-boot key, which invalidates all tokens on restart.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Public origin of the Api, used as the OpenID realm and callback base.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string Issuer { get; set; } = "doggo";
    public string Audience { get; set; } = "doggo-api";
    public int AccessTokenMinutes { get; set; } = 30;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>A configured key must be long enough to be a real signing secret.</summary>
    public bool HasSigningKey => SecretKey.Length >= 32;
}
