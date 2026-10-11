namespace Doggo.Scraper.News;

/// <summary>
/// Fetches the raw text (HTML or XML) of a remote page. Implementations may use
/// plain HTTP or a headless browser for JavaScript-rendered pages.
/// </summary>
public interface IPageFetcher
{
    Task<string> FetchAsync(string url, CancellationToken cancellationToken);
}
