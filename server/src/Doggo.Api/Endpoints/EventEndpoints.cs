using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Analytics;
using Doggo.Infrastructure.Analytics;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

/// <summary>
/// Client event ingest (funnel design §5): anonymous, batch-capped, dictionary-gated,
/// idempotency-key deduplicated and per-visitor rate limited. Ingest failures never block
/// the caller's page; validation problems reject single events, structural problems return 400.
/// </summary>
public static class EventEndpoints
{
    private const int MaxBatchSize = 50;
    private const long MaxBodyBytes = 256 * 1024;
    private const int MaxPropertiesJsonBytes = 2 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events");

        group.MapPost("/", async (
            [FromBody] EventBatchRequest? request,
            [FromServices] DoggoDbContext db,
            [FromServices] EventRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (request?.Events is not { Count: > 0 })
            {
                return Results.Problem("Event batch is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.Events.Count > MaxBatchSize)
            {
                return Results.Problem($"Event batch exceeds {MaxBatchSize} items.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (context.Request.ContentLength is > MaxBodyBytes)
            {
                return Results.Problem("Event batch body is too large.", statusCode: StatusCodes.Status400BadRequest);
            }

            var visitorKey = VisitorKeys.Compute(
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString());
            if (!rateLimiter.Allow(visitorKey))
            {
                return Results.Problem("Too many event batches.", statusCode: StatusCodes.Status429TooManyRequests);
            }

            var client = ParseClient(context.Request.Headers["X-Doggo-Client"]);
            var headerChannel = context.Request.Headers["X-Doggo-Channel"].ToString();
            var userId = Guid.TryParse(context.User.FindFirstValue("sub"), out var sub) ? sub : (Guid?)null;

            var accepted = 0;
            var duplicates = 0;
            var rejected = 0;
            var pending = new List<AnalyticsEvent>();
            var seenKeys = new HashSet<string>();
            var now = DateTime.UtcNow;

            foreach (var item in request.Events)
            {
                if (!IsAcceptable(item, now))
                {
                    rejected++;
                    continue;
                }

                if (!seenKeys.Add(item.IdempotencyKey))
                {
                    duplicates++;
                    continue;
                }

                var propertiesJson = FilterProperties(item.EventName, item.Properties);
                if (Encoding.UTF8.GetByteCount(propertiesJson) > MaxPropertiesJsonBytes)
                {
                    rejected++;
                    continue;
                }

                pending.Add(new AnalyticsEvent
                {
                    EventName = item.EventName,
                    OccurredAtUtc = item.OccurredAtUtc,
                    ReceivedAtUtc = now,
                    SessionId = item.SessionId,
                    UserId = userId,
                    VisitorKey = visitorKey,
                    Client = client,
                    Channel = NormalizeChannel(item.Channel, headerChannel),
                    PropertiesJson = propertiesJson,
                    IdempotencyKey = item.IdempotencyKey,
                    SchemaVersion = item.SchemaVersion,
                    Path = StripQuery(item.Path)
                });
                accepted++;
            }

            // Idempotent ingest: replays already stored count as duplicates, not errors.
            var keys = pending.Select(e => e.IdempotencyKey).ToList();
            var existing = keys.Count == 0
                ? []
                : (await db.AnalyticsEvents.AsNoTracking()
                    .Where(e => keys.Contains(e.IdempotencyKey))
                    .Select(e => e.IdempotencyKey)
                    .ToListAsync(cancellationToken)).ToHashSet();

            foreach (var e in pending)
            {
                if (existing.Contains(e.IdempotencyKey))
                {
                    duplicates++;
                    accepted--;
                }
                else
                {
                    db.AnalyticsEvents.Add(e);
                }
            }

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(new EventBatchResponse(accepted, duplicates, rejected));
        });

        return app;
    }

    private static bool IsAcceptable(EventIngestItem item, DateTime now) =>
        AnalyticsEventDictionary.IsKnown(item.EventName, item.SchemaVersion)
        && !string.IsNullOrWhiteSpace(item.IdempotencyKey)
        && item.OccurredAtUtc <= now.Add(ClockSkew)
        && item.OccurredAtUtc >= now.Subtract(MaxAge);

    private static string FilterProperties(string eventName, Dictionary<string, object>? properties)
    {
        if (properties is not { Count: > 0 })
        {
            return "{}";
        }

        AnalyticsEventDictionary.TryGetWhitelist(eventName, out var whitelist);
        var filtered = properties
            .Where(kv => whitelist.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        return filtered.Count == 0 ? "{}" : JsonSerializer.Serialize(filtered);
    }

    private static AnalyticsClient ParseClient(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "miniapp" => AnalyticsClient.Miniapp,
        "mobile" => AnalyticsClient.Mobile,
        "admin" => AnalyticsClient.Admin,
        _ => AnalyticsClient.Web
    };

    private static string NormalizeChannel(string? itemChannel, string headerChannel)
    {
        var channel = string.IsNullOrWhiteSpace(itemChannel) ? headerChannel : itemChannel;
        return string.IsNullOrWhiteSpace(channel) ? "organic" : channel.Trim();
    }

    private static string StripQuery(string? path)
    {
        var value = path ?? string.Empty;
        var queryStart = value.IndexOf('?');
        return queryStart >= 0 ? value[..queryStart] : value;
    }
}

public sealed record EventBatchRequest(IReadOnlyList<EventIngestItem> Events);

public sealed record EventIngestItem(
    string EventName,
    DateTime OccurredAtUtc,
    Guid SessionId,
    string? Channel,
    int SchemaVersion,
    string IdempotencyKey,
    string? Path,
    Dictionary<string, object>? Properties);

public sealed record EventBatchResponse(int Accepted, int Duplicates, int Rejected);

/// <summary>Fixed-window per-visitor limit for event batches (design §5.1); single instance, in-memory.</summary>
public sealed class EventRateLimiter(int limit = 30, int windowSeconds = 60)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _hits = [];
    private readonly TimeSpan _window = TimeSpan.FromSeconds(Math.Max(windowSeconds, 1));

    public bool Allow(string key)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_hits.TryGetValue(key, out var hits))
            {
                _hits[key] = hits = new Queue<DateTimeOffset>();
            }

            while (hits.Count > 0 && now - hits.Peek() >= _window)
            {
                hits.Dequeue();
            }

            if (hits.Count >= limit)
            {
                return false;
            }

            hits.Enqueue(now);
            return true;
        }
    }
}
