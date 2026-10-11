using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Doggo.Scraper.News;

/// <summary>
/// Inserts the built-in sources on first run. Disabled with Scraper:SeedDefaultSources=false;
/// sources are matched by name, so existing rows are left alone.
/// </summary>
public sealed class NewsSourceSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NewsSourceSeeder> logger) : IHostedService
{
    public static readonly (string Name, NewsSourceType Type, GameKind Game, string Url, string ConfigJson, int Interval)[] Defaults =
    [
        ("cs2-official-blog", NewsSourceType.Rss, GameKind.Cs2,
            "https://blog.counter-strike.net/feed/", "{\"category\":\"news\"}", 30),
        ("cs2-updates", NewsSourceType.Rss, GameKind.Cs2,
            "https://blog.counter-strike.net/category/updates/feed/", "{\"category\":\"update\"}", 60),
        ("dota2-steam-news", NewsSourceType.JsonApi, GameKind.Dota2,
            "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/",
            "{\"appId\":570,\"feedName\":\"steam_community_announcements\",\"count\":20}", 60),
        ("dota2-patch-notes", NewsSourceType.JsonApi, GameKind.Dota2,
            "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/",
            "{\"appId\":570,\"feedName\":\"steam_community_announcements\",\"tag\":\"patchnotes\",\"count\":20}", 60),
        ("cs2-esports-dust2", NewsSourceType.Rss, GameKind.Cs2,
            "https://www.dust2.us/rss", "{\"category\":\"esports\"}", 60),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Scraper:SeedDefaultSources", true))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();

        var existing = await db.NewsSources.Select(s => s.Name).ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
        var created = new List<NewsSource>();

        foreach (var (name, type, game, url, configJson, interval) in Defaults)
        {
            if (existing.Contains(name))
            {
                continue;
            }

            created.Add(new NewsSource
            {
                Id = Guid.CreateVersion7(),
                Name = name,
                SourceType = type,
                Game = game,
                Url = url,
                ConfigJson = configJson,
                FetchIntervalMinutes = interval,
                Enabled = true,
            });
        }

        if (created.Count > 0)
        {
            db.NewsSources.AddRange(created);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} default news sources", created.Count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
