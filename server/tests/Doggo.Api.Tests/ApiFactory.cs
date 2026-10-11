using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>
/// Boots the real Api host with the MySQL context swapped for the EF InMemory provider.
/// Redis stays unresolved unless a test hits /healthz, which none do.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Unique per factory instance: xunit creates one fixture per test class, and the EF
    // InMemory store is global per name, so a shared name would let classes wipe each other.
    private readonly string _databaseName = $"doggo-api-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<DoggoDbContext>)
                            || d.ServiceType == typeof(IDbContextOptionsConfiguration<DoggoDbContext>))
                .ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<DoggoDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}

public abstract class ApiTestBase : IClassFixture<ApiFactory>, IAsyncLifetime
{
    protected ApiTestBase(ApiFactory factory) => Factory = factory;

    protected ApiFactory Factory { get; }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        await db.Database.EnsureDeletedAsync();
    }
}
