using Doggo.Domain.Orders;
using Doggo.Infrastructure;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Vapor;
using Doggo.Worker;
using Doggo.Worker.Market;
using Doggo.Worker.Orders;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOptions<VaporOptions>().Bind(builder.Configuration.GetSection(VaporOptions.SectionName));
builder.Services.AddOptions<MarketSyncOptions>().Bind(builder.Configuration.GetSection(MarketSyncOptions.SectionName));
builder.Services.AddOptions<OrderOptions>().Bind(builder.Configuration.GetSection(OrderOptions.SectionName));
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
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddHostedService<MarketSyncWorker>();
builder.Services.AddHostedService<OrderTimeoutWorker>();
builder.Services.AddHostedService<OrderReconciliationWorker>();

var host = builder.Build();
host.Run();
