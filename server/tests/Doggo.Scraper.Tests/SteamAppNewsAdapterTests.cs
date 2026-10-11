using Doggo.Scraper.News.Adapters;

namespace Doggo.Scraper.Tests;

public class SteamAppNewsAdapterTests
{
    private const string ApiPayload = """
        {
          "appnews": {
            "appid": 570,
            "newsitems": [
              {
                "gid": "1",
                "title": "Dota 2 Update",
                "url": "https://steamcommunity.com/games/570/announcements/detail/1",
                "is_external_url": false,
                "author": "",
                "contents": "Fixed a crash\nAdded a feature",
                "feedlabel": "Dota 2",
                "date": 1760000000,
                "feedname": "steam_community_announcements",
                "feed_type": 1,
                "appid": 570,
                "tags": ["patchnotes"]
              },
              {
                "gid": "2",
                "title": "Repost in russian",
                "url": "https://external.example.com/post",
                "is_external_url": true,
                "author": "",
                "contents": "...",
                "feedlabel": "PlayGround.ru",
                "date": 1759990000,
                "feedname": "PlayGround.ru",
                "feed_type": 0,
                "appid": 570
              }
            ],
            "count": 2
          }
        }
        """;

    [Fact]
    public void Feed_name_filter_drops_third_party_reposts()
    {
        var config = new SteamNewsConfig(570, FeedName: "steam_community_announcements");
        var items = SteamAppNewsAdapter.Parse(ApiPayload, config);

        var item = Assert.Single(items);
        Assert.Equal("Dota 2 Update", item.Title);
    }

    [Fact]
    public void Tag_filter_keeps_only_matching_items()
    {
        var config = new SteamNewsConfig(570, Tag: "patchnotes");
        var items = SteamAppNewsAdapter.Parse(ApiPayload, config);

        var item = Assert.Single(items);
        Assert.Equal("Dota 2 Update", item.Title);
        Assert.Equal("update", item.Category);
    }

    [Fact]
    public void Without_filter_all_items_are_kept()
    {
        var items = SteamAppNewsAdapter.Parse(ApiPayload, new SteamNewsConfig(570));

        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Patchnotes_tag_maps_to_update_category()
    {
        var config = new SteamNewsConfig(570, FeedName: "steam_community_announcements");
        var items = SteamAppNewsAdapter.Parse(ApiPayload, config);

        Assert.Equal("update", items[0].Category);
        Assert.Equal("patchnotes", items[0].Tags);
    }

    [Fact]
    public void Plain_text_contents_become_paragraphs()
    {
        var config = new SteamNewsConfig(570, FeedName: "steam_community_announcements");
        var items = SteamAppNewsAdapter.Parse(ApiPayload, config);

        Assert.NotNull(items[0].ContentHtml);
        Assert.StartsWith("<p>", items[0].ContentHtml);
        Assert.Contains("<br/>", items[0].ContentHtml);
        Assert.DoesNotContain("Fixed a crash\n", items[0].ContentHtml);
    }

    [Fact]
    public void Unix_date_is_converted_to_utc()
    {
        var config = new SteamNewsConfig(570, FeedName: "steam_community_announcements");
        var items = SteamAppNewsAdapter.Parse(ApiPayload, config);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000).UtcDateTime, items[0].PublishedAtUtc);
    }

    [Fact]
    public void Malformed_json_returns_empty()
    {
        Assert.Empty(SteamAppNewsAdapter.Parse("{broken", new SteamNewsConfig(570)));
    }

    [Fact]
    public void Config_defaults_when_missing_or_broken()
    {
        Assert.Equal(0, SteamNewsConfig.Parse(null).AppId);
        Assert.Equal(20, SteamNewsConfig.Parse("{\"appId\":730}").Count);
        Assert.Equal(730, SteamNewsConfig.Parse("{\"appId\":730}").AppId);
        Assert.Equal(0, SteamNewsConfig.Parse("{nope").AppId);
    }
}
