using System.Security.Claims;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Analytics;

namespace Doggo.Api;

/// <summary>
/// Enqueues one UserVisitLog entry per request (after authentication, so the user id is known
/// when a valid token was presented). The write itself is batched and off the request path by
/// <see cref="VisitLogBatchWorker"/>, and health checks, analytics ingest and webhook traffic
/// are skipped — they are not visits. A failure can never break the response.
/// </summary>
public sealed class VisitLogMiddleware(RequestDelegate next, VisitLogSink sink)
{
    private static readonly string[] ExcludedPrefixes =
    [
        "/healthz",
        "/api/events",
        "/api/vapor/webhook",
        "/api/payments/callback"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        finally
        {
            if (ShouldLog(context.Request.Path.Value))
            {
                sink.TryAdd(new VisitLogEntry(
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    IsGuid(context.User.FindFirstValue("sub"), out var userId) ? userId : null,
                    VisitorKey(context),
                    context.Request.Path.Value ?? string.Empty,
                    AnalyticsClients.ParseClient(context.Request.Headers[AnalyticsClients.ClientHeader])));
            }
        }
    }

    private static bool ShouldLog(string? path) =>
        path is { Length: > 0 }
        && !ExcludedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsGuid(string? value, out Guid userId) => Guid.TryParse(value, out userId);

    /// <summary>Stable per (ip, user agent) key, hashed so raw addresses are not stored.</summary>
    private static string VisitorKey(HttpContext context) =>
        VisitorKeys.Compute(
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString());
}
