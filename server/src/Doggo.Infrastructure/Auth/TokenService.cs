using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Doggo.Infrastructure.Auth;

/// <summary>
/// Issues access tokens and builds the validation parameters the Api binds to. Registered
/// as a singleton: the resolved key must be identical for issuance and validation, so the
/// ephemeral fallback (no configured secret) stays stable for the whole process lifetime.
/// </summary>
public sealed class TokenService
{
    private readonly AuthOptions _options;
    private readonly string _signingKey;

    public TokenService(IOptions<AuthOptions> optionsFactory)
    {
        _options = optionsFactory.Value;
        _signingKey = _options.HasSigningKey
            ? _options.SecretKey
            : Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    /// <summary>Effective signing key: the configured secret, or the ephemeral per-boot key.</summary>
    public string SigningKey => _signingKey;

    private SecurityKey SecurityKey => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(Math.Max(_options.AccessTokenMinutes, 1));

    public (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(Guid userId)
    {
        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())], "Bearer"),
            IssuedAt = DateTime.UtcNow,
            Expires = expiresAt,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256)
        });

        return (token, expiresAt);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        IssuerSigningKey = SecurityKey,
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.Audience,
        ClockSkew = TimeSpan.FromMinutes(1),
        ValidateLifetime = true,
        ValidateIssuer = true,
        ValidateAudience = true
    };
}
