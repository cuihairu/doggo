using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Doggo.Api.Tests;

/// <summary>
/// Production boot rules: the mock payment provider verifies no callback, so a production
/// host must fail fast instead of running payments on a channel anyone can forge. Development
/// and the test hosts keep the mock (every other test drives the mock callback flow).
/// </summary>
public class PaymentProviderProductionTests
{
    /// <summary>
    /// A production-shaped host: the same InMemory DbContext swap as the default factory, but a
    /// real connection string (the committed appsettings password is a placeholder that
    /// <c>AddDoggoInfrastructure</c> already rejects outside Development/Testing).
    /// </summary>
    private sealed class ProductionApiFactory : ApiFactory
    {
        protected override IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=127.0.0.1;Database=doggo;Uid=root;Password=prod-secret"
        };
    }

    [Fact]
    public void Production_host_refuses_the_mock_payment_provider()
    {
        // The committed appsettings password is a placeholder that AddDoggoInfrastructure
        // already rejects outside Development/Testing, so give the production host a real one
        // (the DbContext is swapped to InMemory anyway; only the placeholder check matters).
        const string connectionString = "Server=127.0.0.1;Database=doggo;Uid=root;Password=prod-secret";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        try
        {
            using var factory = new ProductionApiFactory();
            var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

            var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());
            Assert.Contains("Mock", exception.GetBaseException().Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        }
    }
}
