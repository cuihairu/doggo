using Doggo.Domain.Price;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;

namespace Doggo.PriceCrawler;

/// <summary>Admin routes (design §8): map review, series + trend query, spread report.</summary>
public static class PriceCrawlerAdminEndpoints
{
    public static IEndpointRouteBuilder MapPriceCrawlerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/price-crawl")
            .RequireAuthorization(new AuthorizeAttribute { Policy = "Admin" });

        group.MapGet("/maps", async (
            IPriceCrawlMapStore maps,
            [FromQuery] string? status = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 50,
            CancellationToken cancellationToken = default) =>
        {
            var parsed = Enum.TryParse<PriceMapStatus>(status, ignoreCase: true, out var parsedStatus) ? parsedStatus : (PriceMapStatus?)null;
            var rows = await maps.ListAsync(parsed, skip < 0 ? 0 : skip, take is > 0 and <= 200 ? take : 50, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(rows);
        });

        group.MapPost("/maps/{id:guid}/confirm", async (Guid id, IPriceCrawlMapStore maps, CancellationToken cancellationToken) =>
        {
            var map = await maps.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (map is null)
            {
                return Results.Problem(statusCode: 404, detail: "map not found");
            }

            map.MapStatus = PriceMapStatus.Confirmed;
            map.MatchSource = PriceMapSource.Manual;
            await maps.SaveAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(map);
        });

        group.MapPost("/maps/{id:guid}/reject", async (Guid id, IPriceCrawlMapStore maps, CancellationToken cancellationToken) =>
        {
            var map = await maps.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (map is null)
            {
                return Results.Problem(statusCode: 404, detail: "map not found");
            }

            map.MapStatus = PriceMapStatus.Rejected;
            map.MatchSource = PriceMapSource.Manual;
            await maps.SaveAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(map);
        });

        group.MapGet("/prices/{marketItemId:guid}", async (
            Guid marketItemId,
            DateTime? from,
            DateTime? to,
            IPriceCrawlSeriesStore series,
            CancellationToken cancellationToken) =>
        {
            var points = await series.QueryByMarketItemAsync(marketItemId, from, to, cancellationToken).ConfigureAwait(false);
            var daily = PriceTrend.DailyCloses(points);
            var trend = PriceTrend.Build(daily);
            return Results.Ok(new
            {
                marketItemId,
                points,
                daily,
                trend,
                periodChange7dPct = PriceTrend.PeriodChangePct(daily, 7),
            });
        });

        group.MapGet("/spread-report", async (int? days, IPriceCrawlQueries queries, CancellationToken cancellationToken) =>
        {
            var rows = await queries.SpreadReportAsync(days is > 0 and <= 90 ? days.Value : 7, cancellationToken).ConfigureAwait(false);
            return Results.Ok(rows);
        });

        return app;
    }
}
