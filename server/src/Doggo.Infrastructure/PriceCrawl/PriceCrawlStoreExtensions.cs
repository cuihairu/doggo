using Doggo.PriceCrawler;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Infrastructure.PriceCrawl;

public static class PriceCrawlStoreExtensions
{
    /// <summary>Registers the EF-backed crawler stores. Both the Scraper host and the Api need them.</summary>
    public static IServiceCollection AddPriceCrawlStores(this IServiceCollection services)
    {
        services.AddScoped<IPriceCrawlMapStore, PriceCrawlMapStore>();
        services.AddScoped<IPriceCrawlRawStore, PriceCrawlRawStore>();
        services.AddScoped<IPriceCrawlSeriesStore, PriceCrawlSeriesStore>();
        services.AddScoped<IPriceCrawlSiteStateStore, PriceCrawlSiteStateStore>();
        services.AddScoped<IPriceCrawlQueries, PriceCrawlQueries>();
        services.AddScoped<IExchangeRateReader, ExchangeRateReader>();
        return services;
    }
}
