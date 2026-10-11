namespace Doggo.Scraper.News;

/// <summary>
/// A single entry as returned by a source adapter, before deduplication and storage.
/// </summary>
public sealed record NewsCandidate(
    string Title,
    string Url,
    DateTime PublishedAtUtc,
    string Category,
    string Tags = "",
    string? Summary = null,
    string? ContentHtml = null,
    string? Author = null,
    string? CoverUrl = null);
