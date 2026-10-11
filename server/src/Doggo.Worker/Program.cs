using Doggo.Infrastructure;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Vapor;
using Doggo.Worker;
using Doggo.Worker.Market;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOptions<VaporOptions>().Bind(builder.Configuration.GetSection(VaporOptions.SectionName));
builder.Services.AddOptions<MarketSyncOptions>().Bind(builder.Configuration.GetSection(MarketSyncOptions.SectionName));
builder.Services.AddHttpClient<VaporClient>(http =>
{
    var options = builder.Configuration
        .GetSection(VaporOptions.SectionName)
        .Get<VaporOptions>() ?? new VaporOptions();
    if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
    {
        http.BaseAddress = baseUri;
    }

    http.Timeout = TimeSpan.FromSeconds(Math.Max(options.TimeoutSeconds, 1));
});
builder.Services.AddScoped<MarketStore>();
builder.Services.AddHostedService<MarketSyncWorker>();

var host = builder.Build();
host.Run();
