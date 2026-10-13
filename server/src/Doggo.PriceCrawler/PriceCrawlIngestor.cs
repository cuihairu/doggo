using Doggo.Domain.Price;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.PriceCrawler;

public sealed record IngestResult(int Fetched, int RawStored, int SeriesUpserted, int SkippedUnconfirmed, int SkippedInvalid, int Suspect);

/// <summary>
/// Fetch → raw capture → clean → series (design §6). Raw rows land for every parsed price;
/// only Confirmed mappings produce series points, and series values are CNY-normalized
/// (ConvertedCents = price × rate) so they compare against MarketPriceDaily directly.
/// </summary>
public sealed class PriceCrawlIngestor(
    IPriceCrawlMapStore maps,
    IPriceCrawlRawStore raws,
    IPriceCrawlSeriesStore series,
    IExchangeRateReader exchangeRates,
    IOptions<PriceCrawlerOptions> options,
    ILogger logger)
{
    private readonly PriceCrawlerOptions _options = options.Value;

    public async Task<IngestResult> IngestAsync(ISiteAdapter adapter, IReadOnlyList<string> externalIds, CancellationToken cancellationToken)
    {
        var fetched = await adapter.FetchPricesAsync(externalIds, cancellationToken).ConfigureAwait(false);
        var result = new IngestResult(fetched.Count, 0, 0, 0, 0, 0);

        foreach (var price in fetched)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var map = await maps.FindAsync(adapter.SiteKey, price.ExternalId, cancellationToken).ConfigureAwait(false);
            if (map?.MapStatus == PriceMapStatus.Rejected)
            {
                continue;
            }

            var observed = price.ObservedAtUtc.UtcDateTime;
            await raws.AddAsync(new PriceCrawlRaw
            {
                SiteKey = adapter.SiteKey,
                ExternalId = price.ExternalId,
                PriceCents = price.PriceCents,
                CurrencyCode = price.CurrencyCode,
                RawPriceText = price.RawPriceText ?? string.Empty,
                PageFingerprint = price.PageFingerprint ?? string.Empty,
                HttpStatusCode = 200,
                ObservedAtUtc = observed,
                FetchedAtUtc = DateTime.UtcNow,
                AdapterVersion = adapter.AdapterVersion,
            }, cancellationToken).ConfigureAwait(false);
            result = result with { RawStored = result.RawStored + 1 };

            if (map is null)
            {
                // Unknown external id: create the auto mapping so admins can confirm it later.
                map = await maps.GetOrAddAsync(adapter.SiteKey, price.ExternalId, string.Empty, string.Empty, cancellationToken).ConfigureAwait(false);
            }

            if (map.MapStatus != PriceMapStatus.Confirmed)
            {
                result = result with { SkippedUnconfirmed = result.SkippedUnconfirmed + 1 };
                continue;
            }

            if (map.MarketItemId is not { } marketItemId)
            {
                // Confirmed maps must carry their target item; a missing link is a data bug.
                logger.LogWarning("Confirmed map {MapId} has no MarketItemId; series skipped", map.Id);
                result = result with { SkippedInvalid = result.SkippedInvalid + 1 };
                continue;
            }

            if (!PriceCleaning.IsPriceValid(price.PriceCents, _options.MaxPriceCents))
            {
                logger.LogWarning("Invalid price {Cents} for {Site}/{ExternalId}; raw kept, series skipped",
                    price.PriceCents, adapter.SiteKey, price.ExternalId);
                result = result with { SkippedInvalid = result.SkippedInvalid + 1 };
                continue;
            }

            if (!PriceCleaning.TryNormalizeCurrency(price.CurrencyCode, out var currency))
            {
                logger.LogWarning("Unknown currency {Currency} for {Site}/{ExternalId}; series skipped",
                    price.CurrencyCode, adapter.SiteKey, price.ExternalId);
                result = result with { SkippedInvalid = result.SkippedInvalid + 1 };
                continue;
            }

            decimal? rate;
            if (currency == "CNY")
            {
                rate = 1m;
            }
            else
            {
                rate = await exchangeRates.GetRateToCnyAsync(currency, cancellationToken).ConfigureAwait(false);
                if (rate is null)
                {
                    logger.LogWarning("No ExchangeRate for {Currency}; raw kept, series skipped (site {Site})",
                        currency, adapter.SiteKey);
                    result = result with { SkippedInvalid = result.SkippedInvalid + 1 };
                    continue;
                }
            }

            var recent = await series.RecentCompareCentsAsync(map.Id, _options.RollingMedianPoints, cancellationToken).ConfigureAwait(false);
            var suspect = PriceCleaning.IsSuspect(price.PriceCents, recent, _options.SuspectThresholdPct);
            var converted = PriceCleaning.ConvertCents(price.PriceCents, rate);

            var written = await series.UpsertWindowedAsync(new PriceCrawlSeries
            {
                MapId = map.Id,
                MarketItemId = marketItemId,
                SiteKey = adapter.SiteKey,
                ExternalId = price.ExternalId,
                PriceCents = price.PriceCents,
                CurrencyCode = currency,
                ConvertedCents = converted,
                ExchangeRate = rate,
                ObservedAtUtc = observed,
                WindowKey = PriceCleaning.WindowKey(observed, _options.DedupWindowMinutes),
                IsSuspect = suspect,
            }, cancellationToken).ConfigureAwait(false);

            if (written)
            {
                result = result with { SeriesUpserted = result.SeriesUpserted + 1 };
            }

            if (suspect)
            {
                result = result with { Suspect = result.Suspect + 1 };
            }
        }

        return result;
    }
}
