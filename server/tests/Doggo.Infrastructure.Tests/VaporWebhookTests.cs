using System.Text;
using System.Text.Json;
using Doggo.Infrastructure.Vapor;

namespace Doggo.Infrastructure.Tests;

public class VaporWebhookTests
{
    private const string Secret = "test-secret";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static (string Body, string Timestamp, string Signature) Signed(
        string body,
        string? secret = null,
        long? unixSeconds = null)
    {
        var timestamp = (unixSeconds ?? Now.ToUnixTimeSeconds()).ToString();
        var signature = Convert.ToHexString(VaporWebhook.ComputeSignature(secret ?? Secret, timestamp, body))
            .ToLowerInvariant();
        return (body, timestamp, $"sha256={signature}");
    }

    private static bool Verify((string Body, string Timestamp, string Signature) request, string secret = Secret, int tolerance = 300) =>
        VaporWebhook.VerifySignature(request.Body, request.Timestamp, request.Signature, secret, tolerance, Now);

    [Fact]
    public void Valid_signature_passes()
    {
        Assert.True(Verify(Signed("""{"id":"x"}""")));
    }

    [Fact]
    public void Tampered_body_fails()
    {
        var signed = Signed("""{"id":"x"}""");
        var tampered = signed with { Body = """{"id":"y"}""" };
        Assert.False(Verify(tampered));
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        Assert.False(Verify(Signed("""{"id":"x"}"""), secret: "other-secret"));
    }

    [Fact]
    public void Stale_timestamp_fails()
    {
        var old = Signed("""{"id":"x"}""", unixSeconds: Now.AddSeconds(-301).ToUnixTimeSeconds());
        Assert.False(Verify(old));

        var withinTolerance = Signed("""{"id":"x"}""", unixSeconds: Now.AddSeconds(-300).ToUnixTimeSeconds());
        Assert.True(Verify(withinTolerance));
    }

    [Fact]
    public void Future_timestamp_beyond_tolerance_fails()
    {
        var future = Signed("""{"id":"x"}""", unixSeconds: Now.AddSeconds(301).ToUnixTimeSeconds());
        Assert.False(Verify(future));
    }

    [Fact]
    public void Missing_prefix_fails()
    {
        var (_, timestamp, signature) = Signed("""{"id":"x"}""");
        var withoutPrefix = signature["sha256=".Length..];
        Assert.False(VaporWebhook.VerifySignature("""{"id":"x"}""", timestamp, withoutPrefix, Secret, 300, Now));
    }

    [Fact]
    public void Uppercase_hex_fails()
    {
        var (_, timestamp, signature) = Signed("""{"id":"x"}""");
        var upper = signature.ToUpperInvariant();
        Assert.False(VaporWebhook.VerifySignature("""{"id":"x"}""", timestamp, upper, Secret, 300, Now));
    }

    [Fact]
    public void Missing_or_malformed_headers_fail()
    {
        var body = """{"id":"x"}""";
        Assert.False(VaporWebhook.VerifySignature(body, null, "sha256=00", Secret, 300, Now));
        Assert.False(VaporWebhook.VerifySignature(body, "not-a-number", "sha256=" + new string('a', 64), Secret, 300, Now));
        Assert.False(VaporWebhook.VerifySignature(body, "123", "tooshort", Secret, 300, Now));
        Assert.False(VaporWebhook.VerifySignature(body, "123", "sha256=" + new string('g', 64), Secret, 300, Now));
    }

    [Fact]
    public void Empty_secret_rejects_everything()
    {
        var signed = Signed("""{"id":"x"}""");
        Assert.False(Verify(signed, secret: ""));
    }

    [Fact]
    public void Envelope_deserializes_from_vapor_json()
    {
        var json = """
            {"id":"019a0000-0000-7000-8000-000000000001","category":"jobs","type":"job_finished",
             "jobId":"019a0000-0000-7000-8000-000000000002","accountName":"acc1","state":"finished",
             "message":"","timestamp":"2026-10-11T12:00:00+00:00","payload":{"app_id":"730"}}
            """;

        var envelope = JsonSerializer.Deserialize<VaporWebhookEnvelope>(json, VaporJson.Options);

        Assert.NotNull(envelope);
        Assert.Equal("jobs", envelope.Category);
        Assert.Equal("job_finished", envelope.Type);
        Assert.Equal(Guid.Parse("019a0000-0000-7000-8000-000000000002"), envelope.JobId);
        Assert.Equal("finished", envelope.State);
        Assert.NotNull(envelope.Payload);
        Assert.Equal("730", envelope.Payload.Value.GetProperty("app_id").GetString());
    }

    [Fact]
    public void Envelope_without_optional_fields_deserializes()
    {
        var json = """{"id":"019a0000-0000-7000-8000-000000000003","category":"market","type":"price_alert","timestamp":"2026-10-11T12:00:00+00:00"}""";

        var envelope = JsonSerializer.Deserialize<VaporWebhookEnvelope>(json, VaporJson.Options);

        Assert.NotNull(envelope);
        Assert.Null(envelope.JobId);
        Assert.Null(envelope.Payload);
    }
}
