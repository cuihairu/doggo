using System.Text.Json;
using Doggo.Infrastructure.Cases;
using Doggo.Infrastructure.Vapor;
using Microsoft.Extensions.Options;

namespace Doggo.Api.Endpoints;

public static class VaporEndpoints
{
    public static IEndpointRouteBuilder MapVaporEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/vapor/webhook", HandleWebhook);
        return app;
    }

    /// <summary>
    /// Receives vapor webhook events: verifies the HMAC signature, drops replays through the
    /// idempotency store, then dispatches to the consuming handlers (case-opening results).
    /// </summary>
    private static async Task<IResult> HandleWebhook(
        HttpRequest request,
        IOptions<VaporOptions> optionsAccessor,
        IWebhookIdempotencyStore idempotency,
        CaseOpeningWebhookHandler caseOpening,
        OrderRetrievalWebhookHandler orderRetrieval,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Doggo.Api.Endpoints.Vapor");
        var options = optionsAccessor.Value;
        if (options.WebhookSecret.Length == 0)
        {
            // Fail closed: an unsigned deployment must not accept webhooks at all.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        string body;
        using (var reader = new StreamReader(request.Body))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var timestamp = request.Headers[VaporWebhook.TimestampHeader].FirstOrDefault();
        var signature = request.Headers[VaporWebhook.SignatureHeader].FirstOrDefault();
        if (!VaporWebhook.VerifySignature(
                body,
                timestamp,
                signature,
                options.WebhookSecret,
                options.WebhookTimestampToleranceSeconds,
                DateTimeOffset.UtcNow))
        {
            return Results.Unauthorized();
        }

        VaporWebhookEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaporWebhookEnvelope>(body, VaporJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "vapor webhook body was not a valid envelope");
            return Results.BadRequest(new { message = "Invalid webhook envelope." });
        }

        if (envelope is null || envelope.Id == Guid.Empty)
        {
            return Results.BadRequest(new { message = "Invalid webhook envelope." });
        }

        var claimKey = $"vapor:webhook:{envelope.Id}";
        // Processing claims are short-lived so a delivery that never completes (crash, or a
        // release that could not reach Redis) self-expires and is reprocessed; a completed
        // delivery is deduped for a week.
        var completedTtl = TimeSpan.FromDays(7);
        var claim = await idempotency
            .TryClaimAsync(claimKey, TimeSpan.FromMinutes(5), completedTtl, cancellationToken)
            .ConfigureAwait(false);
        if (!claim.Claimed)
        {
            if (claim.State == WebhookClaimState.Completed)
            {
                logger.LogInformation("vapor webhook {WebhookId} is a replay of {Category}/{Type}", envelope.Id, envelope.Category, envelope.Type);
                return Results.Ok(new { ok = true, duplicate = true });
            }

            // Still being processed by another delivery. Acknowledging it now would let vapor
            // stop retrying while the original may still fail and release its claim, losing the
            // event; signal retry instead.
            logger.LogInformation("vapor webhook {WebhookId} is in flight ({Category}/{Type})", envelope.Id, envelope.Category, envelope.Type);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        logger.LogInformation(
            "vapor webhook {WebhookId}: {Category}/{Type} job={JobId} account={AccountName} state={State} message={Message}",
            envelope.Id, envelope.Category, envelope.Type, envelope.JobId, envelope.AccountName, envelope.State, envelope.Message);

        if (envelope.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            logger.LogDebug("vapor webhook {WebhookId} payload: {Payload}", envelope.Id, payload.GetRawText());
        }

        try
        {
            await caseOpening.HandleAsync(envelope, cancellationToken);
            await orderRetrieval.HandleAsync(envelope, cancellationToken);
        }
        catch
        {
            // The event was not applied. Release the claim so vapor's retry can reprocess it
            // instead of being acknowledged as a duplicate.
            await idempotency.ReleaseAsync(claimKey, cancellationToken);
            throw;
        }

        // Settle the claim as completed so later replays are acknowledged as duplicates.
        await idempotency.CompleteAsync(claimKey, completedTtl, cancellationToken);
        return Results.Ok(new { ok = true, duplicate = false });
    }
}
