using Doggo.Infrastructure;
using Doggo.Scraper;
using Doggo.Scraper.News;
using Doggo.Scraper.News.Adapters;
using Microsoft.Extensions.Http;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);

builder.Services.AddHttpClient("scraper", http =>
{
    http.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Scraper:FetchTimeoutSeconds", 60));
});

builder.Services.AddHttpClient<RssSourceAdapter>();
builder.Services.AddHttpClient<SteamAppNewsAdapter>();
builder.Services.AddSingleton<HtmlSanitizer>(sp =>
    new HtmlSanitizer(new HtmlSanitizerOptions
    {
        ImageProxyBaseUrl = sp.GetRequiredService<IConfiguration>().GetValue<string?>("Scraper:ImageProxyBaseUrl", null),
    }));
builder.Services.AddTransient<INewsSourceAdapter, RssSourceAdapter>();
builder.Services.AddTransient<INewsSourceAdapter, SteamAppNewsAdapter>();
builder.Services.AddTransient<INewsSourceAdapter, HtmlSourceAdapter>();
builder.Services.AddScoped<SourceAdapterRegistry>();
builder.Services.AddScoped<NewsStore>();
builder.Services.AddScoped<ScraperScheduler>();
builder.Services.AddOptions<ScraperOptions>().Bind(builder.Configuration.GetSection(ScraperOptions.SectionName));

if (builder.Configuration.GetValue("Scraper:EnablePlaywright", false))
{
    builder.Services.AddSingleton<IPageFetcher, PlaywrightPageFetcher>();
}
else
{
    builder.Services.AddHttpClient<IPageFetcher, HttpPageFetcher>();
}

builder.Services.AddHostedService<NewsSourceSeeder>();
builder.Services.AddHostedService<ScraperWorker>();

var host = builder.Build();
host.Run();
