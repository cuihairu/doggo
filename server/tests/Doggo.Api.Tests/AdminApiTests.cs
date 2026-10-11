using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>The /api/admin surface is gated on the role claim minted from IsAdmin.</summary>
public class AdminApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private async Task<string> RegisterAsync(string account)
    {
        var client = Factory.CreateClient();
        var register = await client.PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();
        return account;
    }

    private async Task<HttpClient> LoginClientAsync(string account)
    {
        var client = Factory.CreateClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        login.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Admin_surface_requires_the_admin_role()
    {
        await RegisterAsync("13900000021");
        var client = await LoginClientAsync("13900000021");

        var forbidden = await client.GetAsync("/api/admin/stats");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Flagged_admin_reads_stats_and_products()
    {
        await RegisterAsync("13900000022");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000022");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }

        // A fresh login reads IsAdmin live and mints the role claim into the token.
        var client = await LoginClientAsync("13900000022");

        var stats = await client.GetAsync("/api/admin/stats");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);

        var products = await client.GetAsync("/api/admin/products");
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);

        var orders = await client.GetAsync("/api/admin/orders?page=1");
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
    }
}
