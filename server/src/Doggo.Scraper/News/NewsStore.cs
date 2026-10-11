using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Scraper.News;

/// <summary>
/// Normalizes adapter output (html cleaning, field truncation), drops duplicates by URL
/// fingerprint, and writes the result together with the source's fetch bookkeeping.
/// </summary>
public sealed class NewsStore(DoggoDbContext db, HtmlSanitizer sanitizer)
{
    public async Task<int> SaveCandidatesAsync(NewsSource source, IReadOnlyList<NewsCandidate> candidates, CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            return 0;
        }

        var seen = new HashSet<string>();
        var incoming = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.Title) && !string.IsNullOrWhiteSpace(c.Url))
            .GroupBy(c => NewsFingerprint.CanonicalizeUrl(c.Url))
            .Select(g => g.First())
            .Select(c => new
            {
                CanonicalUrl = NewsFingerprint.CanonicalizeUrl(c.Url),
                Candidate = c,
            })
            .Where(x => seen.Add(NewsFingerprint.ComputeUrlHash(x.CanonicalUrl)))
            .ToList();

        if (incoming.Count == 0)
        {
            return 0;
        }

        var hashes = incoming.Select(x => NewsFingerprint.ComputeUrlHash(x.CanonicalUrl)).ToHashSet();
        var existing = await db.NewsItems
            .IgnoreQueryFilters()
            .Where(i => hashes.Contains(i.UrlHash))
            .Select(i => i.UrlHash)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newItems = new List<NewsItem>(incoming.Count);
        foreach (var row in incoming)
        {
            if (!existingSet.Add(NewsFingerprint.ComputeUrlHash(row.CanonicalUrl)))
            {
                continue;
            }

            newItems.Add(new NewsItem
            {
                Id = Guid.CreateVersion7(),
                SourceId = source.Id,
                Game = source.Game,
                Title = Cut(row.Candidate.Title, 255) ?? string.Empty,
                Summary = Cut(row.Candidate.Summary, 255),
                ContentHtml = sanitizer.Sanitize(row.Candidate.ContentHtml ?? string.Empty),
                Url = Cut(row.CanonicalUrl, 2048) ?? string.Empty,
                UrlHash = NewsFingerprint.ComputeUrlHash(row.CanonicalUrl),
                Category = Cut(row.Candidate.Category, 255) ?? string.Empty,
                Tags = Cut(row.Candidate.Tags, 255) ?? string.Empty,
                Author = Cut(row.Candidate.Author, 255),
                CoverUrl = Cut(row.Candidate.CoverUrl, 255),
                PublishedAtUtc = row.Candidate.PublishedAtUtc,
                ViewCount = 0,
            });
        }

        db.NewsItems.AddRange(newItems);
        db.NewsSources.Update(source);
        source.LastFetchedAtUtc = DateTime.UtcNow;
        source.ConsecutiveFailures = 0;

        var latest = incoming.Max(x => x.Candidate.PublishedAtUtc);
        var cursor = await db.SyncCursors.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.SourceName == source.Name, cancellationToken);
        if (cursor is null)
        {
            cursor = new SyncCursor { SourceName = source.Name, Cursor = latest.ToString("O") };
            db.SyncCursors.Add(cursor);
        }
        else
        {
            cursor.Cursor = latest.ToString("O");
        }

        await db.SaveChangesAsync(cancellationToken);
        return newItems.Count;
    }

    /// <summary>Truncates to the column's varchar limit; NUL bytes are removed as well.</summary>
    private static string? Cut(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var cleaned = value.Replace("\0", string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
