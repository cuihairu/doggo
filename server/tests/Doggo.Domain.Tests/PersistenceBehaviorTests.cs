using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Domain.Tests;

/// <summary>
/// Verifies the shared SaveChanges behaviors (id generation, audit timestamps,
/// soft-delete filter) against the EF InMemory provider.
/// </summary>
public class PersistenceBehaviorTests
{
    private static DoggoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new DoggoDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_generates_id_and_audit_timestamps()
    {
        await using var db = CreateContext();

        var user = new UserAccount { Nickname = "doggo" };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal(GuidVersion, user.Id.Version);
        Assert.True(user.CreatedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(user.CreatedAtUtc, user.UpdatedAtUtc);
    }

    [Fact]
    public async Task Soft_deleted_entities_are_filtered_from_queries()
    {
        await using var db = CreateContext();

        var user = new UserAccount { Nickname = "bye" };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        user.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Empty(db.UserAccounts.Where(x => x.Nickname == "bye"));
        Assert.NotNull(await db.UserAccounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Nickname == "bye"));
    }

    [Fact]
    public async Task Modified_entities_get_updated_timestamp()
    {
        await using var db = CreateContext();

        var user = new UserAccount { Nickname = "a" };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        var created = user.CreatedAtUtc;
        await Task.Delay(10);
        user.Nickname = "b";
        await db.SaveChangesAsync();

        Assert.Equal(created, user.CreatedAtUtc);
        Assert.True(user.UpdatedAtUtc > created);
    }

    private const byte GuidVersion = 7;
}
