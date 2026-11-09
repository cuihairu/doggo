using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Doggo.Api.Endpoints;

/// <summary>
/// Back-office management surface behind the Admin policy: content, products, orders,
/// users, roll templates, and the dashboard counters. Admins are flagged on UserAccount
/// (IsAdmin); there is no self-service path to become one. The surface is split by facet
/// (content, product, order, user, stats, system) so an edit touches one small file
/// instead of this shared root.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(new AuthorizeAttribute { Policy = "Admin" });

        admin.MapAdminContentEndpoints();
        admin.MapAdminProductEndpoints();
        admin.MapAdminOrderEndpoints();
        admin.MapAdminUserEndpoints();
        admin.MapAdminStatsEndpoints();
        admin.MapAdminSystemEndpoints();

        return app;
    }
}
