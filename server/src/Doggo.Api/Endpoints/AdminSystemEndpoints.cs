using System.Globalization;
using Doggo.Domain.Analytics;
using Doggo.Domain.Cases;
using Doggo.Domain.News;
using Doggo.Domain.Orders;
using Doggo.Domain.Products;
using Doggo.Domain.System;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Api.Endpoints;

/// <summary>
/// Back-office system surface: site settings, payment channels and roll management,
/// behind the shared Admin policy.
/// </summary>
public static class AdminSystemEndpoints
{
    public static IEndpointRouteBuilder MapAdminSystemEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPut("/roll/templates/{id:guid}/enabled", async (
            Guid id,
            [FromBody] TemplateEnabledRequest request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            var template = await db.RollRoomTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (template is null)
            {
                return Results.Problem("Template was not found.", statusCode: StatusCodes.Status404NotFound);
            }

            template.Enabled = request.Enabled;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { template.Id, template.Enabled });
        });
        admin.MapGet("/roll/rooms", async (
            DoggoDbContext db,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken cancellationToken = default) =>
        {
            var query = db.RollRooms.AsNoTracking().OrderByDescending(r => r.StartAtUtc ?? r.EndAtUtc);
            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total, page, pageSize, items });
        });

        admin.MapGet("/roll/rooms/{id:guid}/prizes", async (Guid id, DoggoDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await db.RollRoomPrizes.AsNoTracking()
                .Where(p => p.RoomId == id)
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken)));
        admin.MapGet("/settings", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.SiteSettings.AsNoTracking()
                .OrderBy(s => s.Key)
                .Select(s => new SiteSettingView(s.Key, s.Value, s.ValueType, s.Note))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total = items.Count, items });
        });

        admin.MapPut("/settings/{key}", async (string key, [FromBody] SiteSettingUpsert request, DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var keyError = SiteSettingRules.ValidateKey(key);
            if (keyError is not null)
            {
                return Results.Problem(keyError, statusCode: StatusCodes.Status400BadRequest);
            }

            var valueError = SiteSettingRules.ValidateValue(request.Value, request.ValueType);
            if (valueError is not null)
            {
                return Results.Problem(valueError, statusCode: StatusCodes.Status400BadRequest);
            }

            // One row per key: the console edits a setting in place, so an
            // existing row is overwritten rather than duplicated.
            var setting = await db.SiteSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (setting is null)
            {
                setting = new SiteSetting { Id = Guid.CreateVersion7(), Key = key };
                db.SiteSettings.Add(setting);
            }

            setting.Value = request.Value;
            setting.ValueType = request.ValueType ?? "text";
            setting.Note = request.Note;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new SiteSettingView(setting.Key, setting.Value, setting.ValueType, setting.Note));
        });
        admin.MapGet("/payment-channels", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var items = await db.PaymentChannels.AsNoTracking()
                .OrderBy(c => c.Provider)
                .Select(c => PaymentChannelView.From(c))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { total = items.Count, items });
        });

        admin.MapPut("/payment-channels/{provider}", async (
            string provider,
            [FromBody] PaymentChannelUpsert request,
            DoggoDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var parsed)
                || parsed is PaymentProvider.None or PaymentProvider.Coin or PaymentProvider.Coupon
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem("Unknown payment provider.", statusCode: StatusCodes.Status400BadRequest);
            }

            var configError = PaymentChannelRules.Validate(request);
            if (configError is not null)
            {
                return Results.Problem(configError, statusCode: StatusCodes.Status400BadRequest);
            }

            // One row per provider: the console edits the channel in place.
            var channel = await db.PaymentChannels.FirstOrDefaultAsync(c => c.Provider == parsed, cancellationToken);
            if (channel is null)
            {
                channel = new PaymentChannel { Id = Guid.CreateVersion7(), Provider = parsed };
                db.PaymentChannels.Add(channel);
            }

            channel.MerchantId = request.MerchantId;
            channel.AppId = request.AppId ?? string.Empty;
            channel.FeeBps = request.FeeBps;
            channel.SecretRef = request.SecretRef ?? string.Empty;
            channel.Enabled = request.Enabled;
            channel.Note = request.Note;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(PaymentChannelView.From(channel));
        });
        return admin;
    }

public sealed record TemplateEnabledRequest(bool Enabled);
public sealed record SiteSettingView(string Key, string Value, string ValueType, string? Note);
public sealed record SiteSettingUpsert(string Value, string? Note = null, string? ValueType = null);
/// <summary>
/// Site settings are free-form key/value rows, so the guard rails are on the
/// shape instead of a fixed key list: dotted lower-case keys, bounded values,
/// and a closed set of value types the console knows how to render.
/// </summary>
public static class SiteSettingRules
{
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 5000;
    public const int MaxNoteLength = 200;

    public static readonly string[] ValueTypes = ["text", "number", "bool", "json", "url"];

    public static string? ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            return "Setting key must be 1-64 characters.";
        }

        foreach (var c in key)
        {
            var allowed = char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';
            if (!allowed)
            {
                return "Setting key accepts letters, digits, dot, underscore and dash only.";
            }
        }

        return null;
    }

    public static string? ValidateValue(string? value, string? valueType)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Setting value must not be empty.";
        }

        if (value.Length > MaxValueLength)
        {
            return $"Setting value must be at most {MaxValueLength} characters.";
        }

        if (valueType is not null && !ValueTypes.Contains(valueType))
        {
            return $"Setting valueType must be one of {string.Join(", ", ValueTypes)}.";
        }

        return null;
    }
}
public sealed record PaymentChannelUpsert(
    string MerchantId,
    string? AppId = null,
    int FeeBps = 0,
    string? SecretRef = null,
    bool Enabled = true,
    string? Note = null);
public sealed record PaymentChannelView(
    string Provider,
    string MerchantId,
    string AppId,
    int FeeBps,
    string SecretRef,
    bool Enabled,
    string? Note)
{
    public static PaymentChannelView From(PaymentChannel channel) => new(
        channel.Provider.ToString().ToLowerInvariant(),
        channel.MerchantId,
        channel.AppId,
        channel.FeeBps,
        channel.SecretRef,
        channel.Enabled,
        channel.Note);
}
/// <summary>
/// Guard rails for channel rows: bounded ids, fee inside 0-10000 bps, and a secret
/// reference shaped like an env-var name — the secret value itself is never accepted
/// here, because sensitive configuration only travels through environment variables.
/// </summary>
public static class PaymentChannelRules
{
    public const int MaxIdLength = 64;
    public const int MaxFeeBps = 10_000;
    public const int MaxNoteLength = 200;

    public static string? Validate(PaymentChannelUpsert request)
    {
        if (string.IsNullOrWhiteSpace(request.MerchantId) || request.MerchantId.Length > MaxIdLength)
        {
            return "Merchant id must be 1-64 characters.";
        }

        if (request.AppId is { Length: > MaxIdLength })
        {
            return $"App id must be at most {MaxIdLength} characters.";
        }

        if (request.FeeBps is < 0 or > MaxFeeBps)
        {
            return $"Fee must be 0-{MaxFeeBps} basis points.";
        }

        if (request.SecretRef is { Length: > 0 })
        {
            if (request.SecretRef.Length > MaxIdLength)
            {
                return $"Secret ref must be at most {MaxIdLength} characters.";
            }

            foreach (var c in request.SecretRef)
            {
                var allowed = char.IsAsciiLetterOrDigit(c) || c == '_';
                if (!allowed)
                {
                    return "Secret ref accepts an env-var-like name (letters, digits, underscore) — never the secret value.";
                }
            }
        }

        if (request.Note is { Length: > MaxNoteLength })
        {
            return $"Note must be at most {MaxNoteLength} characters.";
        }

        return null;
    }
}
}
