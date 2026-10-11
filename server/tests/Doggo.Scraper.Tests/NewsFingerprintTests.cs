using Doggo.Scraper.News;

namespace Doggo.Scraper.Tests;

public class NewsFingerprintTests
{
    [Fact]
    public void Tracking_params_and_host_case_map_to_same_hash()
    {
        var noisy = "HTTPS://Blog.Example.com/post/?utm_source=rss&utm_medium=feed&id=7";
        var clean = "https://blog.example.com/post/?id=7";

        Assert.Equal(NewsFingerprint.ComputeUrlHash(clean), NewsFingerprint.ComputeUrlHash(noisy));
    }

    [Fact]
    public void Fragment_is_ignored()
    {
        var withFragment = NewsFingerprint.ComputeUrlHash("https://example.com/post/?id=7#comments");
        var withoutFragment = NewsFingerprint.ComputeUrlHash("https://example.com/post/?id=7");

        Assert.Equal(withoutFragment, withFragment);
    }

    [Fact]
    public void Trailing_slash_is_ignored()
    {
        var withSlash = NewsFingerprint.ComputeUrlHash("https://example.com/post/");
        var withoutSlash = NewsFingerprint.ComputeUrlHash("https://example.com/post");

        Assert.Equal(withoutSlash, withSlash);
    }

    [Fact]
    public void Non_tracking_query_is_preserved()
    {
        var a = NewsFingerprint.ComputeUrlHash("https://example.com/post/?id=7&page=2");
        var b = NewsFingerprint.ComputeUrlHash("https://example.com/post/?id=7");

        Assert.NotEqual(b, a);
    }

    [Fact]
    public void Different_paths_differ()
    {
        var a = NewsFingerprint.ComputeUrlHash("https://example.com/post/1");
        var b = NewsFingerprint.ComputeUrlHash("https://example.com/post/2");

        Assert.NotEqual(b, a);
    }

    [Fact]
    public void Hash_is_sha256_hex()
    {
        var hash = NewsFingerprint.ComputeUrlHash("https://example.com/");

        Assert.Equal(64, hash.Length);
        Assert.All(hash, c => Assert.True(Uri.IsHexDigit(c)));
    }
}
