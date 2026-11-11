using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>The /api/admin/settings surface: free-form key/value rows, one row per key.</summary>
public class AdminSettingsApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Password = "pass-word-1";

    private async Task<HttpClient> AdminClientAsync()
    {
        var account = $"135{Random.Shared.Next(100000, 999999):D6}";
        var register = await Factory.CreateClient().PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { account, password = Password }),
            Encoding.UTF8,
            "application/json"));
        register.EnsureSuccessStatusCode();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var user = await db.UserAccounts.SingleAsync(u => u.Phone == account);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        }

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
    public async Task Upsert_creates_a_setting_then_overwrites_it_in_place()
    {
        var client = await AdminClientAsync();

        var first = await client.PutAsJsonAsync("/api/admin/settings/site.announcement", new
        {
            value = "Season starts Friday",
            valueType = "text",
            note = "home banner"
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var created = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("site.announcement", created.GetProperty("key").GetString());
        Assert.Equal("Season starts Friday", created.GetProperty("value").GetString());

        var second = await client.PutAsJsonAsync("/api/admin/settings/site.announcement", new
        {
            value = "Season starts Saturday",
            valueType = "text",
            note = "home banner"
        });
        second.EnsureSuccessStatusCode();

        var list = JsonDocument.Parse(await (await client.GetAsync("/api/admin/settings"))
            .Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Season starts Saturday",
            list.GetProperty("items")[0].GetProperty("value").GetString());

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            Assert.Equal(1, await db.SiteSettings.CountAsync(s => s.Key == "site.announcement"));
        }
    }

    [Fact]
    public async Task Upsert_rejects_invalid_keys_and_empty_values()
    {
        var client = await AdminClientAsync();

        var badKey = await client.PutAsJsonAsync("/api/admin/settings/bad%20key", new
        {
            value = "x",
            valueType = "text",
            note = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        Assert.Contains("letters, digits", await badKey.Content.ReadAsStringAsync());

        var emptyValue = await client.PutAsJsonAsync("/api/admin/settings/site.ok", new
        {
            value = "",
            valueType = "text",
            note = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyValue.StatusCode);
    }
}
