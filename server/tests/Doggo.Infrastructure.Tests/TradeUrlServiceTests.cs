using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Users;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Infrastructure.Tests;

public class TradeUrlServiceTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"trade-url-tests-{Guid.NewGuid():N}")
            .Options);

    private static TradeUrlService CreateService(DoggoDbContext db) => new(db);

    private const string ValidUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1000000001&token=tokentoken1";

    [Fact]
    public async Task Add_parses_url_and_marks_first_link_preferred()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl, "primary"));
        var second = await service.AddAsync(new AddTradeUrlCommand(UserId, "https://steamcommunity.com/tradeoffer/new/?partner=1000000002&token=tokentoken2"));

        Assert.True(first.IsPreferred);
        Assert.False(second.IsPreferred);
        Assert.Equal("76561198960265729", first.SteamId);
        Assert.Equal("1000000001", first.Partner);
        Assert.Equal("tokentoken1", first.Token);
        Assert.Equal(TradeUrlStatus.Unchecked, first.Status);
    }

    [Fact]
    public async Task Add_rejects_duplicate_and_malformed_urls()
    {
        using var db = CreateDb();
        var service = CreateService(db);

        await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl));
        await Assert.ThrowsAsync<TradeUrlConflictException>(() => service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl)));
        await Assert.ThrowsAsync<TradeUrlValidationException>(() => service.AddAsync(new AddTradeUrlCommand(UserId, "https://example.com/tradeoffer/new/?partner=1&token=abcdef")));
    }

    [Fact]
    public async Task Update_reparses_url_and_resets_check()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var link = await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl));
        await service.CheckAsync(UserId, link.Id);

        var updated = await service.UpdateAsync(UserId, link.Id, new UpdateTradeUrlCommand(
            "https://steamcommunity.com/tradeoffer/new/?partner=1000000003&token=tokentoken3", "renamed", null));

        Assert.Equal("76561198960265731", updated.SteamId);
        Assert.Equal("tokentoken3", updated.Token);
        Assert.Equal("renamed", updated.Nickname);
        Assert.Equal(TradeUrlStatus.Unchecked, updated.Status);
        Assert.Null(updated.CheckedAtUtc);
    }

    [Fact]
    public async Task Update_preferred_clears_previous_preferred()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var first = await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl));
        var second = await service.AddAsync(new AddTradeUrlCommand(UserId, "https://steamcommunity.com/tradeoffer/new/?partner=1000000002&token=tokentoken2"));

        await service.UpdateAsync(UserId, second.Id, new UpdateTradeUrlCommand(null, null, true));

        Assert.True((await service.ListAsync(UserId)).Single(t => t.Id == second.Id).IsPreferred);
        Assert.False((await service.ListAsync(UserId)).Single(t => t.Id == first.Id).IsPreferred);
    }

    [Fact]
    public async Task Check_marks_valid_and_invalid_links()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var valid = await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl));

        // A malformed row can exist in storage through an import; CheckAsync flags it.
        var malformed = new UserTradeUrl
        {
            Id = Guid.CreateVersion7(),
            UserId = UserId,
            Url = "https://steamcommunity.com/tradeoffer/new/?partner=1"
        };
        db.UserTradeUrls.Add(malformed);
        await db.SaveChangesAsync();

        var (validLink, validResult) = await service.CheckAsync(UserId, valid.Id);
        Assert.True(validResult.Valid);
        Assert.Equal("76561198960265729", validResult.SteamId);
        Assert.Equal(TradeUrlStatus.Valid, validLink.Status);
        Assert.NotNull(validLink.CheckedAtUtc);

        var (invalidLink, invalidResult) = await service.CheckAsync(UserId, malformed.Id);
        Assert.False(invalidResult.Valid);
        Assert.NotNull(invalidResult.Error);
        Assert.Equal(TradeUrlStatus.Invalid, invalidLink.Status);
    }

    [Fact]
    public async Task Delete_removes_only_own_links()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var link = await service.AddAsync(new AddTradeUrlCommand(UserId, ValidUrl));

        Assert.True(await service.DeleteAsync(UserId, link.Id));
        Assert.False(await service.DeleteAsync(UserId, link.Id));
        Assert.False(await service.DeleteAsync(Guid.CreateVersion7(), link.Id));
    }

    [Fact]
    public async Task CheckUrl_reports_parts_without_touching_storage()
    {
        var valid = TradeUrlService.CheckUrl(ValidUrl);
        Assert.True(valid.Valid);
        Assert.Equal("76561198960265729", valid.SteamId);

        var invalid = TradeUrlService.CheckUrl("https://steamcommunity.com/tradeoffer/new/?partner=1");
        Assert.False(invalid.Valid);
        Assert.NotNull(invalid.Error);
    }
}
