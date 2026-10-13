using System.Security.Claims;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Doggo.Api;

/// <summary>
/// Writes one UserVisitLog row per request (after authentication, so the user id is known
/// when a valid token was presented). Failures are logged and never break the response.
/// </summary>
public sealed partial class VisitLogMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopeFactory,
    ILogger<VisitLogMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        finally
        {
            await WriteVisitLogAsync(context);
        }
    }

    private async Task WriteVisitLogAsync(HttpContext context)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            db.UserVisitLogs.Add(new UserVisitLog
            {
                Id = Guid.CreateVersion7(),
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                UserId = IsGuid(context.User.FindFirstValue("sub"), out var userId) ? userId : null,
                VisitorKey = VisitorKey(context),
                Path = context.Request.Path.Value ?? string.Empty,
                Client = AnalyticsClients.ParseClient(context.Request.Headers[AnalyticsClients.ClientHeader])
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            LogWriteFailed(context.Request.Path.Value, ex.Message);
        }
    }

    private static bool IsGuid(string? value, out Guid userId) => Guid.TryParse(value, out userId);

    /// <summary>Stable per (ip, user agent) key, hashed so raw addresses are not stored.</summary>
    private static string VisitorKey(HttpContext context) =>
        VisitorKeys.Compute(
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());

    [LoggerMessage(Level = LogLevel.Warning, Message = "visit log for {Path} failed: {Reason}")]
    private partial void LogWriteFailed(string? path, string reason);
}
