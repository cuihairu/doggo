using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Doggo.Infrastructure;

public static class DoggoInfrastructureExtensions
{
    /// <summary>
    /// Registers MySQL (EF Core) and Redis. Provider selection lives here only;
    /// MySql.EntityFrameworkCore is used until Pomelo ships an EF Core 10 build.
    /// </summary>
    public static IServiceCollection AddDoggoInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        // Fail closed outside local dev: the committed appsettings carry a placeholder
        // password that must never reach a production database. Development and the
        // EF InMemory test host are exempt (tests never use this connection string).
        var password = ExtractConnectionStringValue(connectionString, "Password");
        var isPlaceholder = string.IsNullOrEmpty(password)
            || string.Equals(password, "change-me", StringComparison.Ordinal);
        if (isPlaceholder
            && !string.Equals(environment.EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Connection string 'Default' must not use the placeholder password outside Development/Testing.");
        }

        services.AddDbContext<Persistence.DoggoDbContext>(options => options.UseMySQL(connectionString));

        var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));

        return services;
    }

    private static string? ExtractConnectionStringValue(string connectionString, string key)
    {
        var prefix = $"{key}=";
        var index = connectionString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + prefix.Length;
        var end = connectionString.IndexOf(';', start);
        if (end < 0)
        {
            end = connectionString.Length;
        }

        return connectionString[start..end];
    }
}
