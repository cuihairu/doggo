using System.Text.Json;
using Doggo.Domain.News;

namespace Doggo.Scraper.News.Adapters;

public sealed record SteamNewsConfig(int AppId, string? FeedName = null, int Count = 20, string? Tag = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SteamNewsConfig Parse(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new SteamNewsConfig(AppId: 0);
        }

        try
        {
            return JsonSerializer.Deserialize<SteamNewsConfig>(configJson, JsonOptions) ?? new SteamNewsConfig(AppId: 0);
        }
        catch (JsonException)
        {
            return new SteamNewsConfig(AppId: 0);
        }
    }
}

/// <summary>
/// Adapter for the official Steam app news API (ISteamNews/GetNewsForApp, no key required).
/// Only the platform's own announcements are kept when <see cref="SteamNewsConfig.FeedName"/> is set;
/// the API otherwise returns third-party reposts in many languages.
/// </summary>
public sealed class SteamAppNewsAdapter(HttpClient http) : INewsSourceAdapter
{
    public NewsSourceType SourceType => NewsSourceType.JsonApi;

    public async Task<IReadOnlyList<NewsCandidate>> FetchAsync(NewsSource source, CancellationToken cancellationToken)
    {
        var config = SteamNewsConfig.Parse(source.ConfigJson);
        if (config.AppId <= 0)
        {
            throw new InvalidOperationException($"Source '{source.Name}' has no appId configured.");
        }

        var url = $"{source.Url.TrimEnd('/')}?appid={config.AppId}&count={config.Count}&maxlength=0&format=json";
        var json = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return Parse(json, config);
    }

    internal static IReadOnlyList<NewsCandidate> Parse(string json, SteamNewsConfig? config = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("appnews", out var appNews)
                || !appNews.TryGetProperty("newsitems", out var items))
            {
                return [];
            }

            var candidates = new List<NewsCandidate>();
            foreach (var item in items.EnumerateArray())
            {
                var title = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()?.Trim()
                    : null;
                var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var feedName = item.TryGetProperty("feedname", out var feedEl) ? feedEl.GetString()?.Trim() : null;
                if (!string.IsNullOrWhiteSpace(config?.FeedName)
                    && !string.Equals(feedName, config.FeedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tags = item.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                    ? tagsEl.EnumerateArray().Select(t => t.GetString() ?? string.Empty).ToList()
                    : [];
                if (!string.IsNullOrWhiteSpace(config?.Tag)
                    && !tags.Any(t => string.Equals(t, config.Tag, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var contents = item.TryGetProperty("contents", out var contentsEl) ? contentsEl.GetString() : null;

                candidates.Add(new NewsCandidate(
                    Title: title!,
                    Url: url!,
                    PublishedAtUtc: item.TryGetProperty("date", out var dateEl) && dateEl.TryGetInt64(out var unix)
                        ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                        : DateTime.UtcNow,
                    Category: tags.Any(t => t.Contains("patch", StringComparison.OrdinalIgnoreCase)) ? "update" : "news",
                    Tags: string.Join(',', tags),
                    Summary: contents is null ? null : Truncate(contents, 255),
                    ContentHtml: ToHtml(contents),
                    Author: item.TryGetProperty("author", out var authorEl)
                        && !string.IsNullOrWhiteSpace(authorEl.GetString())
                        ? authorEl.GetString()!.Trim()
                        : null,
                    CoverUrl: null));
            }

            return candidates;
        }
    }

    private static string? ToHtml(string? contents)
    {
        if (string.IsNullOrEmpty(contents))
        {
            return null;
        }

        if (contents.Contains('<', StringComparison.Ordinal))
        {
            return contents;
        }

        var lines = contents.Replace("\r\n", "\n").Split('\n');
        return $"<p>{string.Join("<br/>", lines.Select(System.Net.WebUtility.HtmlEncode))}</p>";
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
