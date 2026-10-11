using Doggo.Domain.Orders;
using Doggo.Infrastructure;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Vapor;
using Doggo.Worker;
using Doggo.Worker.Cases;
using Doggo.Worker.Market;
using Doggo.Worker.Marketing;
using Doggo.Worker.Orders;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDoggoInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

builder.Services.AddOptions<VaporOptions>().Bind(builder.Configuration.GetSection(VaporOptions.SectionName));
builder.Services.AddOptions<MarketSyncOptions>().Bind(builder.Configuration.GetSection(MarketSyncOptions.SectionName));
builder.Services.AddOptions<OrderOptions>().Bind(builder.Configuration.GetSection(OrderOptions.SectionName));
builder.Services.AddOptions<RollOptions>().Bind(builder.Configuration.GetSection(RollOptions.SectionName));
builder.Services.AddOptions<CoinOptions>().Bind(builder.Configuration.GetSection(CoinOptions.SectionName));
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
builder.Services.AddScoped<RollService>();
builder.Services.AddScoped<CoinService>();
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddHostedService<MarketSyncWorker>();
builder.Services.AddHostedService<OrderTimeoutWorker>();
builder.Services.AddHostedService<OrderReconciliationWorker>();
builder.Services.AddHostedService<RollRoomWorker>();
builder.Services.AddHostedService<CoinExpiryWorker>();

var host = builder.Build();
host.Run();
