using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class CouponApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(HttpClient Client, Guid UserId)> AuthorizedAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            account = $"137{Random.Shared.Next(100000, 999999):D6}",
            password = "super-secret-1"
        });
        response.EnsureSuccessStatusCode();
        var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());
        return (client, registered.GetProperty("userId").GetGuid());
    }

    private async Task SeedAsync(params object[] entities)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DoggoDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static Coupon Coupon(Guid id, string title, bool enabled = true,
        DateTime? validFromUtc = null, DateTime? validToUtc = null) => new()
    {
        Id = id,
        Title = title,
        Type = CouponType.AmountOff,
        AmountOffCents = 500,
        MinOrderCents = 1000,
        Enabled = enabled,
        ValidFromUtc = validFromUtc,
        ValidToUtc = validToUtc
    };

    [Fact]
    public async Task Coupon_center_lists_only_enabled_coupons_inside_the_window()
    {
        var now = DateTime.UtcNow;
        await SeedAsync(
            Coupon(Guid.CreateVersion7(), "live coupon"),
            Coupon(Guid.CreateVersion7(), "disabled coupon", enabled: false),
            Coupon(Guid.CreateVersion7(), "expired coupon", validToUtc: now.AddHours(-1)),
            Coupon(Guid.CreateVersion7(), "future coupon", validFromUtc: now.AddHours(1)),
            new Coupon
            {
                Id = Guid.CreateVersion7(),
                Title = "deleted coupon",
                Type = CouponType.AmountOff,
                AmountOffCents = 500,
                MinOrderCents = 1000,
                DeletedAtUtc = now
            });

        var client = Factory.CreateClient();
        var response = await client.GetAsync("/api/coupons");
        response.EnsureSuccessStatusCode();

        var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var titles = list.EnumerateArray().Select(c => c.GetProperty("title").GetString()).ToList();
        Assert.Equal(["live coupon"], titles);
    }

    [Fact]
    public async Task Claim_returns_the_coupon_then_hits_the_per_user_limit()
    {
        var couponId = Guid.CreateVersion7();
        await SeedAsync(Coupon(couponId, "claimable coupon"));
        var (client, _) = await AuthorizedAsync();

        var first = await client.PostAsync($"/api/coupons/{couponId}/claim", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var view = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("claimable coupon", view.GetProperty("title").GetString());

        var second = await client.PostAsync($"/api/coupons/{couponId}/claim", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("claim limit", await second.Content.ReadAsStringAsync());

        var mine = await client.GetAsync("/api/coupons/mine");
        mine.EnsureSuccessStatusCode();
        var mineList = JsonDocument.Parse(await mine.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["claimable coupon"], mineList.EnumerateArray().Select(c => c.GetProperty("title").GetString()).ToList());
    }

    [Fact]
    public async Task Claim_rejects_an_unknown_coupon_with_400()
    {
        var (client, _) = await AuthorizedAsync();

        var response = await client.PostAsync($"/api/coupons/{Guid.CreateVersion7()}/claim", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Redeem_rejects_an_unknown_code_with_400()
    {
        var (client, _) = await AuthorizedAsync();

        var response = await client.PostAsJsonAsync("/api/redeem", new { code = "NOPE" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Redeem_coin_code_credits_the_ledger_once_per_user()
    {
        var codeId = Guid.CreateVersion7();
        await SeedAsync(new RedeemCode
        {
            Id = codeId,
            Type = RedeemCodeType.Coin,
            Code = "DOGGO50",
            BatchNo = "B1",
            CoinAmount = 50,
            MaxRedemptions = 5
        });
        var (client, _) = await AuthorizedAsync();

        var first = await client.PostAsJsonAsync("/api/redeem", new { code = " doggo50 " });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var result = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(50, result.GetProperty("coinAmount").GetInt64());
        Assert.Equal(50, result.GetProperty("balance").GetInt64());

        var second = await client.PostAsJsonAsync("/api/redeem", new { code = "DOGGO50" });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("already redeemed", await second.Content.ReadAsStringAsync());
    }
}
