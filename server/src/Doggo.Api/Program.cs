using Doggo.Api.Endpoints;
using Doggo.Infrastructure;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDoggoInfrastructure(builder.Configuration);

builder.Services.AddOptions<VaporOptions>().Bind(builder.Configuration.GetSection(VaporOptions.SectionName));
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

// Webhook replays are dropped via Redis SET NX; when Redis cannot be reached the factory
// falls back to process-local claims so webhook handling never hard-fails on Redis.
builder.Services.AddSingleton<InMemoryWebhookIdempotencyStore>();
builder.Services.AddSingleton<IWebhookIdempotencyStore>(sp =>
{
    try
    {
        var redis = sp.GetRequiredService<IConnectionMultiplexer>();
        return new RedisWebhookIdempotencyStore(redis, sp.GetRequiredService<ILogger<RedisWebhookIdempotencyStore>>());
    }
    catch (RedisException)
    {
        return sp.GetRequiredService<InMemoryWebhookIdempotencyStore>();
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapNewsEndpoints();
app.MapContentEndpoints();
app.MapMarketEndpoints();
app.MapProductEndpoints();
app.MapVaporEndpoints();

app.MapGet("/healthz", async (DoggoDbContext db, IConnectionMultiplexer redis, CancellationToken cancellationToken) =>
{
    var database = await db.Database.CanConnectAsync(cancellationToken);

    var redisOk = await Task.Run(() =>
    {
        try
        {
            return redis.GetServer(redis.GetEndPoints()[0]).Ping().TotalMilliseconds < 1000;
        }
        catch (Exception)
        {
            return false;
        }
    });

    var status = new
    {
        status = database && redisOk ? "ok" : "degraded",
        database,
        redis = redisOk
    };

    return database && redisOk ? Results.Ok(status) : Results.Json(status, statusCode: 503);
});

app.Run();
