using Doggo.PriceCrawler;
using Doggo.PriceCrawler.Html;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the price crawler. Coded adapters are registered by the host before calling this;
/// config-driven sites (Html block present) each get a <see cref="ConfiguredHtmlSiteAdapter"/>.
/// </summary>
public static class PriceCrawlerServiceCollectionExtensions
{
    public static IServiceCollection AddPriceCrawler(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PriceCrawlerOptions>()
            .Bind(configuration.GetSection(PriceCrawlerOptions.SectionName))
            .Validate(o => o.ScanIntervalSeconds >= 10, "ScanIntervalSeconds must be at least 10")
            .Validate(o => o.GlobalConcurrentFetches >= 1, "GlobalConcurrentFetches must be positive");

        services.AddHttpClient("pricecrawler", client =>
        {
            var section = configuration.GetSection(PriceCrawlerOptions.SectionName);
            client.Timeout = TimeSpan.FromSeconds(section.GetValue("FetchTimeoutSeconds", 60));
            client.DefaultRequestHeaders.UserAgent.ParseAdd(section.GetValue("UserAgent", "DoggoPriceBot/1.0 (+price-crawler)"));
        });

        // Singleton: the per-origin robots.txt cache must outlive individual resolutions.
        services.AddSingleton<Doggo.PriceCrawler.Robots.RobotsChecker>(sp => new Doggo.PriceCrawler.Robots.RobotsChecker(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("pricecrawler"),
            sp.GetRequiredService<IOptions<Doggo.PriceCrawler.PriceCrawlerOptions>>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Doggo.PriceCrawler.Robots.RobotsChecker>>()));

        var crawler = configuration.GetSection(PriceCrawlerOptions.SectionName).Get<PriceCrawlerOptions>() ?? new PriceCrawlerOptions();
        foreach (var site in crawler.Sites.Where(s => s.Enabled))
        {
            if (site.Html.BaseUrl.Length == 0)
            {
                continue; // coded adapter expected; scheduler warns when none is registered
            }

            services.AddTransient<ISiteAdapter>(sp => new ConfiguredHtmlSiteAdapter(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("pricecrawler"),
                site.Key,
                site.Html,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfiguredHtmlSiteAdapter>>()));
        }

        services.AddScoped<SiteAdapterRegistry>();
        services.AddScoped<PriceCrawlerScheduler>();
        services.AddHostedService<PriceCrawlerWorker>();

        return services;
    }
}
