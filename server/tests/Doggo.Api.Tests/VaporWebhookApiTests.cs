using System.Net;
using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Vapor;

namespace Doggo.Api.Tests;

public class VaporWebhookApiTests : IClassFixture<VaporWebhookApiFactory>
{
    private const string Secret = "test-webhook-secret";
    private readonly VaporWebhookApiFactory _factory;

    public VaporWebhookApiTests(VaporWebhookApiFactory factory) => _factory = factory;

    private static HttpRequestMessage SignedRequest(Guid webhookId, string? secret = Secret, string? body = null)
    {
        var payload = body ?? JsonSerializer.Serialize(new
        {
            id = webhookId,
            category = "jobs",
            type = "job_finished",
            jobId = (Guid?)null,
            accountName = "acc1",
            state = "finished",
            message = "",
            timestamp = DateTimeOffset.UtcNow,
            payload = new { app_id = "730" }
        });

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = Convert.ToHexString(VaporWebhook.ComputeSignature(secret ?? Secret, timestamp, payload))
            .ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vapor/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add(VaporWebhook.TimestampHeader, timestamp);
        request.Headers.Add(VaporWebhook.SignatureHeader, $"sha256={signature}");
        return request;
    }

    private static async Task<JsonElement> SendAndReadJsonAsync(HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }

    [Fact]
    public async Task Signed_webhook_is_accepted_once()
    {
        var client = _factory.CreateClient();
        var webhookId = Guid.NewGuid();

        var body = await SendAndReadJsonAsync(client, SignedRequest(webhookId));
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.False(body.GetProperty("duplicate").GetBoolean());

        // A replay of the same event id is acknowledged but flagged as duplicate.
        var replayBody = await SendAndReadJsonAsync(client, SignedRequest(webhookId));
        Assert.True(replayBody.GetProperty("duplicate").GetBoolean());
    }

    [Fact]
    public async Task Tampered_signature_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var request = SignedRequest(Guid.NewGuid(), secret: "wrong-secret");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_webhook_is_unauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/vapor/webhook")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Well_signed_body_that_is_not_an_envelope_is_bad_request()
    {
        var client = _factory.CreateClient();
        var request = SignedRequest(Guid.NewGuid(), body: """{"not":"an envelope"}""");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
