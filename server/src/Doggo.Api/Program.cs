using Doggo.Api.Endpoints;
using Doggo.Infrastructure;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDoggoInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapNewsEndpoints();
app.MapContentEndpoints();

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
