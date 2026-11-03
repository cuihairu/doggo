using Microsoft.AspNetCore.Http;

namespace Doggo.Api.Endpoints;

/// <summary>
/// httpOnly cookie transport for browser (web/admin) sessions. Native clients
/// (mobile/miniapp) keep using the JSON body and Authorization header, so the
/// cookies are only set when the request looks like a browser navigation or XHR.
/// Because the API and the SPA share one origin (nginx reverse proxy) the cookies
/// are SameSite=Lax and never CrossSite, so no CORS credentials setup is needed.
/// </summary>
public static class AuthCookies
{
    public const string AccessTokenCookie = "doggo.access_token";
    public const string RefreshTokenCookie = "doggo.refresh_token";

    private const string AccessTokenPath = "/";
    private const string RefreshTokenPath = "/api/auth";

    public static bool IsBrowser(HttpContext context) =>
        context.Request.Headers["X-Doggo-Client"].ToString() is "web" or "admin";

    /// <summary>Secure only when served over TLS (direct or behind an HTTPS-terminating proxy).</summary>
    private static bool IsSecure(HttpContext context) =>
        context.Request.IsHttps
        || context.Request.Headers["X-Forwarded-Proto"].ToString().Split(',').Contains("https", StringComparer.OrdinalIgnoreCase);

    public static void SetTokens(HttpContext context, string accessToken, DateTime accessExpiresAtUtc, string refreshToken, DateTime refreshExpiresAtUtc)
    {
        if (!IsBrowser(context))
        {
            return;
        }

        context.Response.Cookies.Append(AccessTokenCookie, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(context),
            SameSite = SameSiteMode.Lax,
            Path = AccessTokenPath,
            Expires = accessExpiresAtUtc
        });
        context.Response.Cookies.Append(RefreshTokenCookie, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(context),
            SameSite = SameSiteMode.Lax,
            Path = RefreshTokenPath,
            Expires = refreshExpiresAtUtc
        });
    }

    public static void Clear(HttpContext context)
    {
        if (!IsBrowser(context))
        {
            return;
        }

        context.Response.Cookies.Delete(AccessTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(context),
            SameSite = SameSiteMode.Lax,
            Path = AccessTokenPath
        });
        context.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = IsSecure(context),
            SameSite = SameSiteMode.Lax,
            Path = RefreshTokenPath
        });
    }
}
