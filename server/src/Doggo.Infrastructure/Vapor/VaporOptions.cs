namespace Doggo.Infrastructure.Vapor;

public sealed class VaporOptions
{
    public const string SectionName = "Vapor";

    /// <summary>Base URL of the vapor control plane, e.g. http://127.0.0.1:8080. Empty disables outbound calls.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Bearer token for the control plane REST API (vapor admin api key).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Shared secret used to verify X-Vapor-Signature on inbound webhooks. Empty rejects every webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Allowed clock skew between the X-Vapor-Timestamp header and local time.</summary>
    public int WebhookTimestampToleranceSeconds { get; set; } = 300;

    /// <summary>Default target account name when a dispatch does not name one. Empty lets vapor pick.</summary>
    public string DefaultTarget { get; set; } = string.Empty;

    /// <summary>HTTP timeout for control plane calls.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How long to wait for a dispatched job to finish before giving up.</summary>
    public int JobTimeoutSeconds { get; set; } = 120;

    /// <summary>Poll interval while waiting for a dispatched job.</summary>
    public int JobPollIntervalMilliseconds { get; set; } = 500;
}
