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
/// Back-office statistics surface: dashboard counters, daily trend, product ranking
/// and the four-step funnel, behind the shared Admin policy.
/// </summary>
public static class AdminStatsEndpoints
{
    public static IEndpointRouteBuilder MapAdminStatsEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/stats", async (DoggoDbContext db, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            return Results.Ok(new
            {
                users = await db.UserAccounts.CountAsync(cancellationToken),
                orders = await db.UserOrders.CountAsync(cancellationToken),
                paidOrders = await db.UserOrders.CountAsync(o => o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Cancelled, cancellationToken),
                revenueCents = await db.UserOrders.SumAsync(o => (long?)o.TotalCostCents, cancellationToken) ?? 0,
                newUsersToday = await db.UserAccounts.CountAsync(u => u.CreatedAtUtc >= now.Date, cancellationToken)
            });
        });
        admin.MapGet("/stats/trend", async (
            DoggoDbContext db,
            [FromQuery] int days = 14,
            CancellationToken cancellationToken = default) =>
        {
            var since = DateTime.UtcNow.Date.AddDays(-(Math.Clamp(days, 1, 90) - 1));
            var orders = await db.UserOrders.AsNoTracking()
                .Where(o => o.CreatedAtUtc >= since)
                .Select(o => new { o.CreatedAtUtc, o.TotalCostCents })
                .ToListAsync(cancellationToken);
            var users = await db.UserAccounts.AsNoTracking()
                .Where(u => u.CreatedAtUtc >= since)
                .Select(u => u.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var trend = new List<object>();
            for (var day = since; day <= DateTime.UtcNow.Date; day = day.AddDays(1))
            {
                var d = day;
                trend.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"),
                    newUsers = users.Count(u => u.Date == d),
                    orders = orders.Count(o => o.CreatedAtUtc.Date == d),
                    revenueCents = orders.Where(o => o.CreatedAtUtc.Date == d).Sum(o => o.TotalCostCents)
                });
            }

            return Results.Ok(trend);
        });
        admin.MapGet("/stats/top-products", async (
            DoggoDbContext db,
            [FromQuery] int top = 10,
            CancellationToken cancellationToken = default) =>
        {
            var rows = await db.UserOrders.AsNoTracking()
                .Where(o => o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.ProductId)
                .Select(g => new { ProductId = g.Key, OrderCount = g.Count(), RevenueCents = g.Sum(o => o.TotalCostCents) })
                .ToListAsync(cancellationToken);

            var ids = rows.Select(r => r.ProductId).ToList();
            var names = await db.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);

            return Results.Ok(rows
                .OrderByDescending(r => r.RevenueCents)
                .Take(Math.Clamp(top, 1, 50))
                .Select(r => new
                {
                    productId = r.ProductId,
                    productName = names.GetValueOrDefault(r.ProductId, string.Empty),
                    orderCount = r.OrderCount,
                    revenueCents = r.RevenueCents
                }));
        });
        admin.MapGet("/funnel", async (
            DoggoDbContext db,
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] string? groupBy,
            [FromQuery] string? grain,
            [FromQuery] string? client,
            [FromQuery] string? channel,
            CancellationToken cancellationToken) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var toValue = to is { } toParam && toParam < today ? toParam : today.AddDays(-1);
            var fromValue = from is { } fromParam && fromParam < toValue ? fromParam : toValue.AddDays(-13);
            // At most 90 days per report, matching the stats-area clamp style.
            if (fromValue < toValue.AddDays(-89))
            {
                fromValue = toValue.AddDays(-89);
            }

            var query = db.FunnelDailyStats.AsNoTracking()
                .Where(s => s.Date >= fromValue && s.Date <= toValue);
            if (Enum.TryParse<AnalyticsClient>(client, ignoreCase: true, out var clientFilter))
            {
                query = query.Where(s => s.Client == clientFilter);
            }

            if (!string.IsNullOrWhiteSpace(channel))
            {
                query = query.Where(s => s.Channel == channel.Trim());
            }

            var stats = await query.OrderBy(s => s.Date).ToListAsync(cancellationToken);

            var mode = groupBy?.Trim().ToLowerInvariant();
            string KeyOf(FunnelDailyStat s) => mode switch
            {
                "client" => s.Client.ToString().ToLowerInvariant(),
                "channel" => s.Channel,
                _ => "all"
            };

            static decimal Rate(long uv, long firstUv) => firstUv <= 0 ? 0 : Math.Round((decimal)uv / firstUv, 4);

            var steps = new (string Name, string Label, Func<FunnelDailyStat, long> Uv)[]
            {
                ("page_view_product", "浏览商品", s => s.ViewProductUv),
                ("checkout_intent", "加购", s => s.CheckoutIntentUv),
                ("order_created", "下单", s => s.OrderCreatedUv),
                ("order_paid", "支付", s => s.OrderPaidUv)
            };

            var groups = stats
                .GroupBy(KeyOf)
                .Select(g =>
                {
                    var uv = steps.Select(step => g.Sum(step.Uv)).ToArray();
                    return new
                    {
                        key = g.Key,
                        steps = steps.Select((step, i) => new
                        {
                            step = step.Name,
                            label = step.Label,
                            uv = uv[i],
                            rate = i == 0 ? (uv[0] > 0 ? 1m : 0m) : Rate(uv[i], uv[0]),
                            dropRate = i == 0 ? 0m : 1m - Rate(uv[i], uv[0])
                        }).ToArray()
                    };
                })
                .OrderBy(g => g.key)
                .ToArray();

            var isWeek = string.Equals(grain, "week", StringComparison.OrdinalIgnoreCase);
            static string IsoWeek(DateOnly date) => $"{ISOWeek.GetYear(date):0}-W{ISOWeek.GetWeekOfYear(date):00}";
            var rows = stats
                .GroupBy(s => (Period: isWeek ? IsoWeek(s.Date) : s.Date.ToString("yyyy-MM-dd"), Client: KeyOf(s), Channel: s.Channel))
                .Select(g => new
                {
                    date = g.Key.Period,
                    client = g.Key.Client,
                    channel = g.Key.Channel,
                    viewProductUv = g.Sum(s => s.ViewProductUv),
                    checkoutIntentUv = g.Sum(s => s.CheckoutIntentUv),
                    orderCreatedUv = g.Sum(s => s.OrderCreatedUv),
                    orderPaidUv = g.Sum(s => s.OrderPaidUv)
                })
                .ToArray();

            return Results.Ok(new
            {
                from = fromValue.ToString("yyyy-MM-dd"),
                to = toValue.ToString("yyyy-MM-dd"),
                grain = isWeek ? "week" : "day",
                groupBy = mode ?? "none",
                groups,
                rows,
                receivedToday = await db.AnalyticsEvents.AsNoTracking()
                    .CountAsync(e => e.ReceivedAtUtc >= DateTime.UtcNow.Date, cancellationToken)
            });
        });
        return admin;
    }

}
