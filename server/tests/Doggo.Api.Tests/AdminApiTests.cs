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

    [Fact]
    public async Task Admin_management_surfaces_respond()
    {
        await RegisterAsync("13900000023");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000023");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }
        var client = await LoginClientAsync("13900000023");

        foreach (var path in new[]
                 {
                     "/api/admin/news-sources",
                     "/api/admin/news-items?page=1",
                     "/api/admin/notices",
                     "/api/admin/promotions",
                     "/api/admin/exchange-rates",
                     "/api/admin/stats/trend?days=7",
                     "/api/admin/stats/top-products?top=5",
                     "/api/admin/users?page=1",
                     "/api/admin/roll/rooms?page=1",
                     "/api/roll/templates"
                 })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path} -> {(int)response.StatusCode}");
        }

        // Notices round-trip: create then delete.
        var created = await client.PostAsync("/api/admin/notices", new StringContent(
            JsonSerializer.Serialize(new { title = "t", content = "c" }),
            Encoding.UTF8,
            "application/json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var noticeId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
        var deleted = await client.DeleteAsync($"/api/admin/notices/{noticeId}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        // User sub-resources: coin ledger and login history of the admin itself.
        var meId = string.Empty;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            meId = (await db.UserAccounts.SingleAsync(u => u.Phone == "13900000023")).Id.ToString();
        }
        var coinLogs = await client.GetAsync($"/api/admin/users/{meId}/coin-logs?page=1");
        Assert.Equal(HttpStatusCode.OK, coinLogs.StatusCode);
        var loginLogs = await client.GetAsync($"/api/admin/users/{meId}/login-logs?page=1");
        Assert.Equal(HttpStatusCode.OK, loginLogs.StatusCode);
    }

    [Fact]
    public async Task Site_settings_upsert_lists_and_overwrites()
    {
        await RegisterAsync("13900000024");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000024");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }
        var client = await LoginClientAsync("13900000024");

        // Create, then update the same key: one row, last value wins.
        var created = await client.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = "维护通知", note = "首页公告" }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdView = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal("维护通知", createdView.RootElement.GetProperty("value").GetString());
        Assert.Equal("首页公告", createdView.RootElement.GetProperty("note").GetString());

        var updated = await client.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = "已恢复", note = "首页公告" }));
        updated.EnsureSuccessStatusCode();

        var listed = await client.GetStringAsync("/api/admin/settings");
        using var listView = JsonDocument.Parse(listed);
        var rows = listView.RootElement.GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(rows.Where(r => r.GetProperty("key").GetString() == "site.notice"));
        Assert.Equal("已恢复", row.GetProperty("value").GetString());

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            Assert.Equal(1, await db.SiteSettings.CountAsync(s => s.Key == "site.notice"));
        }
    }

    [Fact]
    public async Task Site_settings_reject_bad_keys_values_and_anonymous_callers()
    {
        await RegisterAsync("13900000025");
        var guest = await LoginClientAsync("13900000025");
        var forbidden = await guest.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = "x" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000025");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }
        var client = await LoginClientAsync("13900000025");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/settings/site notice", JsonBody(new { value = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = new string('x', 5001) }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/settings/site.notice", JsonBody(new { value = "ok", valueType = "sql" }))).StatusCode);
    }

    [Fact]
    public async Task Payment_channels_upsert_list_and_overwrite()
    {
        await RegisterAsync("13900000026");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000026");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }
        var client = await LoginClientAsync("13900000026");

        var created = await client.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new
        {
            merchantId = "2026010400",
            appId = "doggo-web",
            feeBps = 60,
            secretRef = "ALIPAY_SECRET_KEY",
            enabled = true,
            note = "primary channel"
        }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdView = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal("alipay", createdView.RootElement.GetProperty("provider").GetString());
        Assert.Equal("ALIPAY_SECRET_KEY", createdView.RootElement.GetProperty("secretRef").GetString());

        // Upsert again: same provider, one row, last values win.
        var updated = await client.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new
        {
            merchantId = "2026019999",
            appId = "doggo-web",
            feeBps = 55,
            secretRef = "ALIPAY_SECRET_KEY",
            enabled = false
        }));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var listed = await client.GetStringAsync("/api/admin/payment-channels");
        using var listView = JsonDocument.Parse(listed);
        var rows = listView.RootElement.GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(rows.Where(r => r.GetProperty("provider").GetString() == "alipay"));
        Assert.Equal("2026019999", row.GetProperty("merchantId").GetString());
        Assert.Equal(55, row.GetProperty("feeBps").GetInt32());
        Assert.False(row.GetProperty("enabled").GetBoolean());

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            Assert.Equal(1, await db.PaymentChannels.CountAsync(p => p.Provider == Doggo.Domain.Orders.PaymentProvider.Alipay));
        }
    }

    [Fact]
    public async Task Payment_channels_reject_unknown_provider_bad_config_and_guests()
    {
        await RegisterAsync("13900000027");
        var guest = await LoginClientAsync("13900000027");
        var forbidden = await guest.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new { merchantId = "m" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Phone == "13900000027");
            account.IsAdmin = true;
            await db.SaveChangesAsync();
        }
        var client = await LoginClientAsync("13900000027");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/payment-channels/bitcoin", JsonBody(new { merchantId = "m" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new { merchantId = "" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new { merchantId = "m", feeBps = 20000 }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/admin/payment-channels/alipay", JsonBody(new { merchantId = "m", secretRef = "has space" }))).StatusCode);
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
