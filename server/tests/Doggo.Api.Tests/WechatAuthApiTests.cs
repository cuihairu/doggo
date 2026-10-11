using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

/// <summary>The wx.login exchange: 503 when unconfigured, and a working account mint on the happy path.</summary>
public class WechatAuthApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<HttpResponseMessage> PostWechatAsync(string code)
    {
        var client = Factory.CreateClient();
        return await client.PostAsync("/api/auth/wechat", new StringContent(
            JsonSerializer.Serialize(new { code }),
            Encoding.UTF8,
            "application/json"));
    }

    [Fact]
    public async Task Wechat_login_without_credentials_reports_unavailable()
    {
        var response = await PostWechatAsync("any-code");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Wechat_login_requires_a_code()
    {
        var response = await PostWechatAsync(" ");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

/// <summary>Factory with WeChat credentials and a stubbed code2session endpoint.</summary>
public sealed class WechatConfiguredApiFactory : ApiFactory
{
    protected override IDictionary<string, string?> ExtraConfiguration => new Dictionary<string, string?>
    {
        ["WeChat:AppId"] = "wx-test-appid",
        ["WeChat:AppSecret"] = "wx-test-secret"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            // Re-register the typed client with a handler that fakes code2session.
            services.AddHttpClient<IWechatSessionClient, HttpWechatSessionClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubWechatHandler());
        });
    }

    private sealed class StubWechatHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var code = query.Split("js_code=")[1].Split('&')[0];
            var openid = code == "code-returning" ? "oX-returning-user" : $"oX-{code}";
            var body = JsonSerializer.Serialize(new { openid, session_key = "sk", unionid = "u-1", errcode = 0 });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}

public class WechatConfiguredApiTests : IClassFixture<WechatConfiguredApiFactory>
{
    private readonly WechatConfiguredApiFactory _factory;

    public WechatConfiguredApiTests(WechatConfiguredApiFactory factory) => _factory = factory;

    private async Task<(Guid UserId, HttpClient Client)> LoginAsync(string code)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/auth/wechat", new StringContent(
            JsonSerializer.Serialize(new { code, nickname = "wx-player" }),
            Encoding.UTF8,
            "application/json"));
        response.EnsureSuccessStatusCode();
        var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        return (tokens.GetProperty("userId").GetGuid(), client);
    }

    [Fact]
    public async Task Wechat_login_mints_an_account_and_returns_the_same_one_next_time()
    {
        var (firstId, client) = await LoginAsync("code-returning");

        var profile = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal("wx-player", JsonDocument.Parse(await profile.Content.ReadAsStringAsync())
            .RootElement.GetProperty("nickname").GetString());

        // Same WeChat openid = same account.
        var (secondId, _) = await LoginAsync("code-returning");
        Assert.Equal(firstId, secondId);
    }
}
