using Doggo.Domain.News;

namespace Doggo.Scraper.News;

/// <summary>Resolves the adapter responsible for a given source type.</summary>
public sealed class SourceAdapterRegistry(IEnumerable<INewsSourceAdapter> adapters)
{
    private readonly Dictionary<NewsSourceType, INewsSourceAdapter> _byType =
        adapters.ToDictionary(a => a.SourceType);

    public INewsSourceAdapter Get(NewsSourceType sourceType) =>
        _byType.TryGetValue(sourceType, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No adapter registered for source type {sourceType}.");
}
