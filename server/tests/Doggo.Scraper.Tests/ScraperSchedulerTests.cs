using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Doggo.Scraper.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Scraper.Tests;

public sealed class FakeSourceAdapter : INewsSourceAdapter
{
    public NewsSourceType SourceType { get; init; } = NewsSourceType.Rss;
    public int FetchCount { get; private set; }
    public Exception? Throw { get; init; }

    public Task<IReadOnlyList<NewsCandidate>> FetchAsync(NewsSource source, CancellationToken cancellationToken)
    {
        FetchCount++;
        if (Throw is not null)
        {
            return Task.FromException<IReadOnlyList<NewsCandidate>>(Throw);
        }

        IReadOnlyList<NewsCandidate> result = [new NewsCandidate($"Item {FetchCount}", $"https://example.com/{source.Name}/{FetchCount}", DateTime.UtcNow, "news")];
        return Task.FromResult(result);
    }
}

public class ScraperSchedulerTests
{
    private static (ScraperScheduler Scheduler, ServiceProvider Provider, FakeSourceAdapter Adapter) Build(FakeSourceAdapter? adapter = null)
    {
        var fake = adapter ?? new FakeSourceAdapter();
        var services = new ServiceCollection();
        services.AddLogging();
        // Capture the name outside the lambda: the AddDbContext configuration action is
        // re-run per context instance, so a NewGuid() inline would give each scope its own store.
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<DoggoDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<HtmlSanitizer>();
        services.AddScoped<NewsStore>();
        var provider = services.BuildServiceProvider();

        var registry = new SourceAdapterRegistry([fake]);
        var scheduler = new ScraperScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            Options.Create(new ScraperOptions
            {
                CircuitFailureThreshold = 3,
                CircuitCooldownMinutes = 30,
                FetchTimeoutSeconds = 30,
            }),
            provider.GetRequiredService<ILogger<ScraperScheduler>>());

        return (scheduler, provider, fake);
    }

    private static async Task<NewsSource> SeedSourceAsync(IServiceProvider services, Action<NewsSource>? setup = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        var source = new NewsSource
        {
            Id = Guid.CreateVersion7(),
            Name = "s1",
            SourceType = NewsSourceType.Rss,
            Game = GameKind.Cs2,
            Url = "https://example.com/feed",
            FetchIntervalMinutes = 30,
        };
        setup?.Invoke(source);
        db.NewsSources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    private static async Task<NewsSource> ReloadSourceAsync(IServiceProvider services, Guid id)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        return await db.NewsSources.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private static readonly DateTimeOffset Now = new(2025, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Due_source_is_fetched_and_items_are_stored()
    {
        var (scheduler, provider, adapter) = Build();
        var source = await SeedSourceAsync(provider);

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        Assert.Equal(1, adapter.FetchCount);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        Assert.Equal(1, await db.NewsItems.CountAsync());
    }

    [Fact]
    public async Task Source_not_due_is_skipped()
    {
        var (scheduler, provider, adapter) = Build();
        var source = await SeedSourceAsync(provider, s =>
            s.LastFetchedAtUtc = Now.UtcDateTime.AddMinutes(-5));

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        Assert.Equal(0, adapter.FetchCount);
    }

    [Fact]
    public async Task Circuit_opens_after_consecutive_failures()
    {
        var (scheduler, provider, adapter) = Build();
        var source = await SeedSourceAsync(provider, s =>
        {
            s.ConsecutiveFailures = 3;
            s.LastFetchedAtUtc = Now.UtcDateTime.AddMinutes(-5);
        });

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        Assert.Equal(0, adapter.FetchCount);
    }

    [Fact]
    public async Task Circuit_closes_after_cooldown()
    {
        var (scheduler, provider, adapter) = Build();
        var source = await SeedSourceAsync(provider, s =>
        {
            s.ConsecutiveFailures = 3;
            s.LastFetchedAtUtc = Now.UtcDateTime.AddMinutes(-45);
        });

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        Assert.Equal(1, adapter.FetchCount);
    }

    [Fact]
    public async Task Failure_increments_consecutive_failures()
    {
        var (scheduler, provider, _) = Build(new FakeSourceAdapter { Throw = new HttpRequestException("boom") });
        var source = await SeedSourceAsync(provider);

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        var reloaded = await ReloadSourceAsync(provider, source.Id);
        Assert.Equal(1, reloaded.ConsecutiveFailures);
    }

    [Fact]
    public async Task Success_resets_consecutive_failures()
    {
        var (scheduler, provider, _) = Build();
        var source = await SeedSourceAsync(provider, s =>
        {
            s.ConsecutiveFailures = 2;
            s.LastFetchedAtUtc = Now.UtcDateTime.AddMinutes(-60);
        });

        await scheduler.RunSourceAsync(source, Now, CancellationToken.None);

        var reloaded = await ReloadSourceAsync(provider, source.Id);
        Assert.Equal(0, reloaded.ConsecutiveFailures);
        Assert.NotNull(reloaded.LastFetchedAtUtc);
    }

    [Fact]
    public async Task Disabled_sources_are_not_scanned()
    {
        var (scheduler, provider, adapter) = Build();
        await SeedSourceAsync(provider, s => s.Enabled = false);

        await scheduler.RunDueSourcesAsync(Now, CancellationToken.None);

        Assert.Equal(0, adapter.FetchCount);
    }
}
