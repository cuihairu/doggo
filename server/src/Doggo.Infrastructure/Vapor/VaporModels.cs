using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doggo.Infrastructure.Vapor;

/// <summary>Control plane job view (GET /v1/jobs/{id}). Task statuses arrive as camelCase strings.</summary>
public sealed record VaporJobView(
    Guid Id,
    string Action,
    string State,
    string? CreatedBy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VaporJobTaskView> Tasks);

public sealed record VaporJobTaskView(
    Guid Id,
    string Action,
    string Status,
    string? Target,
    JsonElement? Output,
    string? Error,
    int Attempt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed partial class VaporJobStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>Case-insensitive comparison against the camelCase strings vapor serializes enums with.</summary>
    public static bool EqualsAny(string? status, string candidate) =>
        string.Equals(status, candidate, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Output of vapor's get_market_listings action. The control plane stores the plugin
/// output as-is, so top-level keys are snake_case while listing records are camelCase.
/// </summary>
public sealed record VaporMarketListingsView(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("total_count")] long TotalCount,
    [property: JsonPropertyName("start")] long Start,
    [property: JsonPropertyName("page_size")] long PageSize,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("listings")] IReadOnlyList<VaporMarketListingView> Listings);

public sealed record VaporMarketListingView(
    Guid? ListingId,
    string Name,
    string HashName,
    long? SellListings,
    string? AppId,
    string? AssetId,
    string? ClassId,
    string? InstanceId,
    decimal? TotalPrice,
    int? CurrencyId,
    DateTimeOffset FetchedAt);

/// <summary>Payload for the get_market_listings action; keys are snake_case per the vapor action contract.</summary>
public sealed record VaporMarketListingsRequest(
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("start")] long Start,
    [property: JsonPropertyName("count")] long Count);
