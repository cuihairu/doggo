using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Vapor;

/// <summary>Thrown when a control plane call fails or a dispatched job does not succeed.</summary>
public sealed class VaporClientException : Exception
{
    public VaporClientException(string message) : base(message)
    {
    }

    public VaporClientException(string message, Exception inner) : base(message, inner)
    {
    }
}

public static class VaporJson
{
    /// <summary>
    /// Wire settings shared by request and response paths. Vapor serializes with camelCase but
    /// has no naming policy on payloads stored from plugins, so snake_case keys carry explicit
    /// [JsonPropertyName] attributes and everything else binds case-insensitively.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

/// <summary>
/// Typed client for the vapor control plane REST API (POST /v1/jobs, GET /v1/jobs/{id})
/// with dispatch-and-poll helpers for the actions Doggo consumes.
/// </summary>
public sealed class VaporClient
{
    private readonly HttpClient _http;
    private readonly VaporOptions _options;

    public VaporClient(HttpClient http, IOptions<VaporOptions> options)
    {
        _http = http;
        _options = options.Value;

        if (Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) && _http.BaseAddress is null)
        {
            _http.BaseAddress = baseUri;
        }

        if (_options.ApiKey.Length > 0)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    /// <summary>False while BaseUrl or ApiKey is missing; callers treat vapor as unavailable then.</summary>
    public bool IsConfigured => _options.BaseUrl.Length > 0 && _options.ApiKey.Length > 0;

    /// <summary>Submits a job and returns its id (202 Accepted carries {"id": "..."}).</summary>
    public async Task<Guid> DispatchJobAsync(
        string action,
        IReadOnlyList<string>? targets = null,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var body = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["targets"] = ResolveTargets(targets),
            ["payload"] = payload
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("v1/jobs", body, VaporJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new VaporClientException($"vapor dispatch failed: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, "dispatch", cancellationToken).ConfigureAwait(false);

        var view = await response.Content.ReadFromJsonAsync<VaporJobEnvelope>(VaporJson.Options, cancellationToken)
            .ConfigureAwait(false);
        var summary = RequireSummary(view, "dispatch");
        if (summary.Id == Guid.Empty)
        {
            throw new VaporClientException("vapor dispatch returned no job id");
        }

        return summary.Id;
    }

    /// <summary>Fetches the current job view, including per-task statuses and outputs.</summary>
    public async Task<VaporJobView> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"v1/jobs/{jobId}", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new VaporClientException($"vapor get job failed: {ex.Message}", ex);
        }

        await EnsureSuccessAsync(response, $"get job {jobId}", cancellationToken).ConfigureAwait(false);

        var view = await response.Content.ReadFromJsonAsync<VaporJobEnvelope>(VaporJson.Options, cancellationToken)
            ?? throw new VaporClientException($"vapor job {jobId} returned an empty body");
        var summary = RequireSummary(view, $"get job {jobId}");
        return new VaporJobView(summary.Id, summary.Action, summary.Status, null, summary.CreatedAt, view.Tasks ?? []);
    }

    /// <summary>
    /// Dispatches a job and polls until every task reaches a terminal state. Throws when a task
    /// fails or the deadline passes; otherwise returns the final job view.
    /// </summary>
    public async Task<VaporJobView> DispatchAndWaitAsync(
        string action,
        object? payload = null,
        IReadOnlyList<string>? targets = null,
        CancellationToken cancellationToken = default)
    {
        var jobId = await DispatchJobAsync(action, targets, payload, cancellationToken).ConfigureAwait(false);

        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(_options.JobTimeoutSeconds, 1));
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(_options.JobPollIntervalMilliseconds, 50));

        while (true)
        {
            var job = await GetJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            var tasks = job.Tasks ?? [];

            if (tasks.Count > 0 && tasks.All(t => VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Finished)
                                                  || VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Failed)
                                                  || VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Canceled)))
            {
                return job;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new VaporClientException($"vapor job {jobId} ({action}) timed out after {_options.JobTimeoutSeconds}s");
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs get_market_listings and parses the first task output into the listings view.</summary>
    public async Task<VaporMarketListingsView> GetMarketListingsAsync(
        string appId,
        long start,
        long count,
        CancellationToken cancellationToken = default)
    {
        var job = await DispatchAndWaitAsync(
            "get_market_listings",
            new VaporMarketListingsRequest(appId, start, count),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var tasks = job.Tasks ?? [];
        var failed = tasks.FirstOrDefault(t => !VaporJobStatus.EqualsAny(t.Status, VaporJobStatus.Finished));
        if (failed is not null)
        {
            throw new VaporClientException($"vapor job {job.Id} task failed: {failed.Error ?? failed.Status}");
        }

        var output = tasks.Select(t => t.Output).FirstOrDefault(o => o is { ValueKind: JsonValueKind.Object });
        if (output is null)
        {
            throw new VaporClientException($"vapor job {job.Id} produced no listings output");
        }

        try
        {
            return output.Value.Deserialize<VaporMarketListingsView>(VaporJson.Options)
                   ?? throw new VaporClientException($"vapor job {job.Id} listings output was null");
        }
        catch (JsonException ex)
        {
            throw new VaporClientException($"vapor job {job.Id} listings output was malformed: {ex.Message}", ex);
        }
    }

    private string[] ResolveTargets(IReadOnlyList<string>? targets)
    {
        if (targets is { Count: > 0 })
        {
            return [.. targets];
        }

        return _options.DefaultTarget.Length > 0 ? [_options.DefaultTarget] : [];
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new VaporClientException("vapor is not configured: set Vapor:BaseUrl and Vapor:ApiKey");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new VaporClientException($"vapor {what} failed with {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}");
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200] + "…";

    /// <summary>
    /// Vapor's job reply envelope. POST /v1/jobs answers {job: {...}} and GET /v1/jobs/{id}
    /// answers {job: {...}, tasks: [...]}; both serialize job/task shapes in camelCase.
    /// </summary>
    private sealed record VaporJobEnvelope(
        VaporJobSummary? Job,
        IReadOnlyList<VaporJobTaskView>? Tasks);

    private sealed record VaporJobSummary(
        Guid Id,
        string Action,
        string Status,
        DateTimeOffset CreatedAt);

    private static VaporJobSummary RequireSummary(VaporJobEnvelope? envelope, string what) =>
        envelope?.Job ?? throw new VaporClientException($"vapor {what} returned no job summary");
}
