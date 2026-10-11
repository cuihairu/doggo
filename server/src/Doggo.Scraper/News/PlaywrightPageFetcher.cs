using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Doggo.Scraper.News;

/// <summary>
/// Page fetcher for JavaScript-rendered pages, backed by Playwright (Chromium).
/// The browser is launched lazily on first use and reused for the process lifetime.
/// Register this instead of <see cref="HttpPageFetcher"/> when Scraper:EnablePlaywright is true.
/// </summary>
public sealed class PlaywrightPageFetcher(ILogger<PlaywrightPageFetcher> logger) : IPageFetcher, IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<string> FetchAsync(string url, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync(cancellationToken).ConfigureAwait(false);
        await using var page = await browser.NewPageAsync().ConfigureAwait(false);
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle }).ConfigureAwait(false);
        return await page.ContentAsync().ConfigureAwait(false);
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is not null)
        {
            return _browser;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _playwright ??= await Playwright.CreateAsync().ConfigureAwait(false);
            _browser ??= await _playwright.Chromium.LaunchAsync(new() { Headless = true }).ConfigureAwait(false);
            return _browser;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to launch Playwright browser");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync().ConfigureAwait(false);
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _initLock.Dispose();
    }
}
