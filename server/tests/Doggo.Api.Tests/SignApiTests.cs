using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Doggo.Api.Tests;

public class SignApiTests(ApiFactory factory) : ApiTestBase(factory)
{
    private async Task<HttpClient> AuthorizedAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            account = $"136{Random.Shared.Next(100000, 999999):D6}",
            password = "super-secret-1"
        });
        response.EnsureSuccessStatusCode();
        var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", registered.GetProperty("accessToken").GetString());
        return client;
    }

    private static string Day(int offsetFromToday) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offsetFromToday).ToString("O");

    [Fact]
    public async Task Cycle_starts_unsigned_then_today_signs_and_blocks_a_second_sign()
    {
        var client = await AuthorizedAsync();

        var before = await client.GetAsync("/api/sign/");
        before.EnsureSuccessStatusCode();
        var cycle = JsonDocument.Parse(await before.Content.ReadAsStringAsync()).RootElement;
        Assert.False(cycle.GetProperty("todaySigned").GetBoolean());
        Assert.Equal(0, cycle.GetProperty("totalReward").GetInt64());

        var signed = await client.PostAsync("/api/sign/", null);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var result = JsonDocument.Parse(await signed.Content.ReadAsStringAsync()).RootElement;
        Assert.True(result.GetProperty("coinReward").GetInt64() > 0);

        var again = await client.PostAsync("/api/sign/", null);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("already signed", await again.Content.ReadAsStringAsync());

        var after = await client.GetAsync("/api/sign/");
        after.EnsureSuccessStatusCode();
        var nextCycle = JsonDocument.Parse(await after.Content.ReadAsStringAsync()).RootElement;
        Assert.True(nextCycle.GetProperty("todaySigned").GetBoolean());
        Assert.Equal(result.GetProperty("cycleTotalReward").GetInt64(),
            nextCycle.GetProperty("totalReward").GetInt64());
    }

    [Fact]
    public async Task Makeup_rejects_a_future_day_with_400()
    {
        var client = await AuthorizedAsync();

        var response = await client.PostAsJsonAsync("/api/sign/makeup", new
        {
            date = Day(1),
            orderNo = "whatever"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("future day", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Makeup_rejects_a_day_outside_the_current_week_with_400()
    {
        var client = await AuthorizedAsync();

        var response = await client.PostAsJsonAsync("/api/sign/makeup", new
        {
            date = Day(-8),
            orderNo = "whatever"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("current week", await response.Content.ReadAsStringAsync());
    }
}
