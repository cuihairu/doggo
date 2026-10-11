using Doggo.Domain.News;

namespace Doggo.Scraper.News;

/// <summary>
/// Fetches entries from one configured <see cref="NewsSource"/>. Adapters are selected by source type.
/// </summary>
public interface INewsSourceAdapter
{
    NewsSourceType SourceType { get; }

    Task<IReadOnlyList<NewsCandidate>> FetchAsync(NewsSource source, CancellationToken cancellationToken);
}
