using Doggo.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Infrastructure.Tests;

public class DoggoInfrastructureExtensionsTests
{
    private static IConfiguration ConfigWith(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Placeholder_password_allowed_in_dev_and_test(string environment)
    {
        var services = new ServiceCollection();
        var ex = Record.Exception(() =>
            services.AddDoggoInfrastructure(
                ConfigWith("Server=localhost;Port=3306;Database=doggo;User=doggo;Password=change-me;"),
                new StubEnvironment(environment)));
        Assert.Null(ex);
    }

    [Fact]
    public void Placeholder_password_rejected_in_production()
    {
        var services = new ServiceCollection();
        var ex = Record.Exception(() =>
            services.AddDoggoInfrastructure(
                ConfigWith("Server=localhost;Port=3306;Database=doggo;User=doggo;Password=change-me;"),
                new StubEnvironment("Production")));
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Real_password_allowed_in_production()
    {
        var services = new ServiceCollection();
        var ex = Record.Exception(() =>
            services.AddDoggoInfrastructure(
                ConfigWith("Server=localhost;Port=3306;Database=doggo;User=doggo;Password=s3cret;"),
                new StubEnvironment("Production")));
        Assert.Null(ex);
    }

    [Fact]
    public void Empty_password_rejected_in_production()
    {
        var services = new ServiceCollection();
        var ex = Record.Exception(() =>
            services.AddDoggoInfrastructure(
                ConfigWith("Server=localhost;Port=3306;Database=doggo;User=doggo;Password=;"),
                new StubEnvironment("Production")));
        Assert.IsType<InvalidOperationException>(ex);
    }
}
