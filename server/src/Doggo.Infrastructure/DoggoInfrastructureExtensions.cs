using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Doggo.Infrastructure;

public static class DoggoInfrastructureExtensions
{
    /// <summary>
    /// Registers MySQL (EF Core) and Redis. Provider selection lives here only;
    /// MySql.EntityFrameworkCore is used until Pomelo ships an EF Core 10 build.
    /// </summary>
    public static IServiceCollection AddDoggoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<Persistence.DoggoDbContext>(options => options.UseMySQL(connectionString));

        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

        return services;
    }
}
