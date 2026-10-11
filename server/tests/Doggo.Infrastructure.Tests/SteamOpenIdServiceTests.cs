using Doggo.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class SteamOpenIdServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private const string SteamId = "76561198000000001";
    private const string ClaimedId = "https://steamcommunity.com/openid/id/76561198000000001";

    private static SteamOpenIdService CreateService(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var options = Options.Create(new AuthOptions
        {
            SecretKey = "unit-test-signing-secret-that-is-long-enough",
            PublicBaseUrl = "https://doggo.test"
        });
        var handler = new StubHandler
        {
            Responder = responder ?? (_ => throw new InvalidOperationException("no http expected"))
        };
        return new SteamOpenIdService(options, new HttpClient(handler), NullLogger<SteamOpenIdService>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> Responder { get; init; }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Responder(request));
        }
    }

    private static HttpResponseMessage Text(string body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain") };

    [Fact]
    public void BuildLoginUrl_PointsAtSteamWithState()
    {
        var url = CreateService().BuildLoginUrl(UserId, "https://doggo.test");

        Assert.Contains("steamcommunity.com/openid/login", url);
        Assert.Contains("openid.mode=checkid_setup", url);
        Assert.Contains("openid.return_to=https%3A%2F%2Fdoggo.test%2Fapi%2Fauth%2Fsteam%2Fcallback", url);
        Assert.Contains("openid.state=", url);
    }

    [Fact]
    public async Task Callback_WithValidAssertion_BindsAndReturnsIdentity()
    {
        var service = CreateService(_ => Text("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n"));
        var url = service.BuildLoginUrl(UserId, "https://doggo.test");
        var state = url[(url.IndexOf("openid.state=", StringComparison.Ordinal) + "openid.state=".Length)..];

        var query = new List<KeyValuePair<string, string?>>
        {
            new("openid.mode", "id_res"),
            new("openid.claimed_id", ClaimedId),
            new("openid.identity", ClaimedId),
            new("openid.return_to", "https://doggo.test/api/auth/steam/callback"),
            new("openid.response_nonce", "2026-10-11T10:00:00Zabc"),
            new("openid.assoc_handle", "handle"),
            new("openid.signed", "signed,mode,identity,claimed_id,return_to,response_nonce,assoc_handle"),
            new("openid.sig", "signature"),
            new("openid.state", Uri.UnescapeDataString(state))
        };

        var result = await service.VerifyCallbackAsync(query);

        Assert.NotNull(result);
        Assert.Equal(UserId, result!.UserId);
        Assert.Equal(SteamId, result.SteamId);
    }

    [Fact]
    public async Task Callback_WithRejectedAssertion_ReturnsNull()
    {
        var service = CreateService(_ => Text("is_valid:false\n"));
        var url = service.BuildLoginUrl(UserId, "https://doggo.test");
        var state = Uri.UnescapeDataString(url[(url.IndexOf("openid.state=", StringComparison.Ordinal) + "openid.state=".Length)..]);

        var query = new List<KeyValuePair<string, string?>>
        {
            new("openid.claimed_id", ClaimedId),
            new("openid.state", state)
        };

        Assert.Null(await service.VerifyCallbackAsync(query));
    }

    [Fact]
    public async Task Callback_WithTamperedState_ReturnsNull()
    {
        var service = CreateService(_ => Text("is_valid:true\n"));

        var query = new List<KeyValuePair<string, string?>>
        {
            new("openid.claimed_id", ClaimedId),
            new("openid.state", Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "." + Convert.ToBase64String(Guid.NewGuid().ToByteArray()))
        };

        Assert.Null(await service.VerifyCallbackAsync(query));
    }

    [Fact]
    public async Task Callback_WithoutState_ReturnsNull()
    {
        var service = CreateService(_ => Text("is_valid:true\n"));

        var query = new List<KeyValuePair<string, string?>> { new("openid.claimed_id", ClaimedId) };

        Assert.Null(await service.VerifyCallbackAsync(query));
    }

    [Fact]
    public void ExtractSteamId_AcceptsOnlyThe17DigitIdentifier()
    {
        Assert.Equal(SteamId, SteamOpenIdService.ExtractSteamId(ClaimedId));
        Assert.Null(SteamOpenIdService.ExtractSteamId("http://steamcommunity.com/openid/id/76561198000000001"));
        Assert.Null(SteamOpenIdService.ExtractSteamId("https://steamcommunity.com/openid/id/not-a-number"));
        Assert.Null(SteamOpenIdService.ExtractSteamId("https://steamcommunity.com/openid/id/12345"));
        Assert.Null(SteamOpenIdService.ExtractSteamId(null));
        Assert.Null(SteamOpenIdService.ExtractSteamId(string.Empty));
    }
}
