using System.Security.Claims;

namespace Doggo.Api.Endpoints;

/// <summary>Helpers for reading the authenticated caller from the JWT principal.</summary>
public static class EndpointAuth
{
    /// <summary>The authenticated user id from the JWT "sub" claim, or null when anonymous.</summary>
    public static Guid? CurrentUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;
}
