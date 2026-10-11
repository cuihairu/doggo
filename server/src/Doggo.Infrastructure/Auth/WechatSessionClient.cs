using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Auth;

/// <summary>Raised when the WeChat app credentials are not configured — maps to 503.</summary>
public sealed class WechatLoginNotConfiguredException(string message) : Exception(message);

/// <summary>Raised when WeChat rejects the login code — maps to 401.</summary>
public sealed class WechatLoginException(string message) : Exception(message);

public sealed record WechatSession(string OpenId, string? UnionId, string? SessionKey);

/// <summary>Exchanges a wx.login code for the miniapp openid via code2session.</summary>
public interface IWechatSessionClient
{
    Task<WechatSession> ExchangeAsync(string code, CancellationToken cancellationToken = default);
}

public sealed class WechatOptions
{
    public const string SectionName = "WeChat";
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
}

/// <summary>code2session over HTTPS. Credentials come from configuration only; never from the client.</summary>
public sealed class HttpWechatSessionClient(IOptions<WechatOptions> optionsFactory, HttpClient http) : IWechatSessionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly WechatOptions _options = optionsFactory.Value;

    public async Task<WechatSession> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
        {
            throw new WechatLoginNotConfiguredException("WeChat login is not configured on this server.");
        }

        var url = $"https://api.weixin.qq.com/sns/jscode2session?appid={Uri.EscapeDataString(_options.AppId)}&secret={Uri.EscapeDataString(_options.AppSecret)}&js_code={Uri.EscapeDataString(code)}&grant_type=authorization_code";
        var response = await http.GetFromJsonAsync<Code2SessionResponse>(url, JsonOptions, cancellationToken)
            ?? throw new WechatLoginException("WeChat returned an empty session response.");

        if (response.ErrCode is not (null or 0))
        {
            throw new WechatLoginException($"WeChat code2session failed: {response.ErrCode} {response.ErrMsg}");
        }

        if (string.IsNullOrWhiteSpace(response.OpenId))
        {
            throw new WechatLoginException("WeChat session did not include an openid.");
        }

        return new WechatSession(response.OpenId, response.UnionId, response.SessionKey);
    }

    private sealed record Code2SessionResponse(string? OpenId, string? UnionId, string? SessionKey, int? ErrCode, string? ErrMsg);
}
