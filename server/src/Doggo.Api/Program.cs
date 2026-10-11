using Doggo.Api;
using Doggo.Api.Endpoints;
using Doggo.Domain.Orders;
using Doggo.Infrastructure;
using Doggo.Infrastructure.Auth;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Market;
using Doggo.Infrastructure.Marketing;
using Doggo.Infrastructure.Orders;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Users;
using Doggo.Infrastructure.Vapor;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDoggoInfrastructure(builder.Configuration);

builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.SectionName));
// Singleton: issuance and validation must share one signing key, including the ephemeral
// per-boot fallback when no Auth:SecretKey is configured.
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<SteamOpenIdService>();
builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
// Bind bearer options to the singleton TokenService so issuance and validation share one
// signing key, including the ephemeral per-boot fallback when no Auth:SecretKey is set.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TokenService>((options, tokens) =>
    {
        // Keep "sub" as-is instead of remapping to ClaimTypes.NameIdentifier.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.ValidationParameters;
    });

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
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<TradeUrlService>();
builder.Services.AddScoped<GroupBuyingService>();
builder.Services.AddScoped<PrizeService>();
builder.Services.AddScoped<RollService>();
builder.Services.AddScoped<CoinService>();
builder.Services.AddScoped<CouponService>();
builder.Services.AddScoped<InviteService>();
builder.Services.AddScoped<SignService>();
builder.Services.AddOptions<InviteOptions>().Bind(builder.Configuration.GetSection(InviteOptions.SectionName));
builder.Services.AddOptions<SignOptions>().Bind(builder.Configuration.GetSection(SignOptions.SectionName));
builder.Services.AddScoped<CaseOpeningWebhookHandler>();

// Payment channels land with the deployment milestone; mock covers development and tests.
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<VisitLogMiddleware>();

app.MapNewsEndpoints();
app.MapAuthEndpoints();
app.MapCoinEndpoints();
app.MapCouponEndpoints();
app.MapSignEndpoints();
app.MapContentEndpoints();
app.MapMarketEndpoints();
app.MapProductEndpoints();
app.MapVaporEndpoints();
app.MapOrderEndpoints();
app.MapTradeUrlEndpoints();
app.MapCaseEndpoints();
app.MapPrizeEndpoints();
app.MapRollEndpoints();

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
