using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class AuthApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private const string Account = "13900000001";
    private const string Password = "super-secret-1";

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private async Task<JsonElement> RegisterAsync(HttpClient client, string account = Account, string password = Password)
    {
        var response = await client.PostAsync("/api/auth/register", Json(new { account, password }));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var client = Factory.CreateClient();
        var registered = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Register_login_me_refresh_and_logout_run_through_the_api()
    {
        var client = Factory.CreateClient();

        var registered = await RegisterAsync(client);
        Assert.True(registered.GetProperty("refreshToken").GetString()?.Length > 0);

        // Login returns a working token pair too.
        var login = await client.PostAsync("/api/auth/login", Json(new { account = Account, password = Password }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        var accessToken = loginBody.GetProperty("accessToken").GetString()!;
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        // /me requires the bearer token and echoes the profile.
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var authed = Factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profile = await authed.GetStringAsync("/api/auth/me");
        using (var profileDoc = JsonDocument.Parse(profile))
        {
            Assert.Equal(Account, profileDoc.RootElement.GetProperty("phone").GetString());
            Assert.Equal("Active", profileDoc.RootElement.GetProperty("status").GetString());
        }

        // Refresh rotates: the old refresh token stops working, the new one works.
        var refresh = await client.PostAsync("/api/auth/refresh", Json(new { refreshToken }));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var newRefresh = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync()).RootElement
            .GetProperty("refreshToken").GetString()!;

        var replay = await client.PostAsync("/api/auth/refresh", Json(new { refreshToken }));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // Logout revokes the live token; refreshing with it then fails.
        var logout = await client.PostAsync("/api/auth/logout", Json(new { refreshToken = newRefresh }));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var afterLogout = await client.PostAsync("/api/auth/refresh", Json(new { refreshToken = newRefresh }));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Register_duplicate_account_returns_409()
    {
        var client = Factory.CreateClient();
        await RegisterAsync(client);

        var response = await client.PostAsync("/api/auth/register", Json(new { account = Account, password = Password }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Login_wrong_password_returns_401_and_writes_a_failure_log()
    {
        var client = Factory.CreateClient();
        await RegisterAsync(client);

        var response = await client.PostAsync("/api/auth/login", Json(new { account = Account, password = "nope-nope" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Contains(await db.UserLoginLogs.ToListAsync(), l => !l.Success);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/api/auth/register", Json(new { account = Account, password = "short" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Steam_bind_requires_a_token_and_a_configured_base_url()
    {
        var client = Factory.CreateClient();

        // No token: 401 before anything else.
        var unauthorized = await client.GetAsync("/api/auth/steam/login");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        // Token but no Auth:PublicBaseUrl in the test factory: the bind cannot start.
        var authed = await AuthorizedClientAsync();
        var noBaseUrl = await authed.GetAsync("/api/auth/steam/login");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noBaseUrl.StatusCode);

        // A garbage callback is rejected without touching Steam.
        var badCallback = await client.GetAsync("/api/auth/steam/callback?openid.claimed_id=nonsense");
        Assert.Equal(HttpStatusCode.Unauthorized, badCallback.StatusCode);
    }

    [Fact]
    public async Task Requests_are_written_to_the_visit_log()
    {
        var client = await AuthorizedClientAsync();

        await client.GetAsync("/api/auth/me");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var visits = await db.UserVisitLogs.ToListAsync();
        Assert.NotEmpty(visits);
        Assert.Contains(visits, v => v.Path == "/api/auth/me");
        Assert.Contains(visits, v => v.VisitorKey.Length == 32);
    }
}
