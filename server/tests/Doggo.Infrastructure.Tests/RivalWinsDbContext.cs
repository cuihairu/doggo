using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Doggo.Infrastructure.Tests;

/// <summary>
/// A context whose next SaveChanges commits a rival write to the same store first and then
/// fails, simulating a concurrent request winning the guard row while our save is in flight.
/// Our own rows are never applied — the transactional rollback MySQL performs — so a losing
/// flow can be asserted to have left no coins behind.
/// </summary>
internal sealed class RivalWinsDbContext(string store, DbContextOptions<DoggoDbContext> options)
    : DoggoDbContext(options)
{
    public Action<DoggoDbContext>? OnRivalSave { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (OnRivalSave is not null)
        {
            var rival = OnRivalSave;
            OnRivalSave = null;
            await using var rivalDb = new DoggoDbContext(new DbContextOptionsBuilder<DoggoDbContext>()
                .UseInMemoryDatabase(store)
                .Options);
            rival(rivalDb);
            await rivalDb.SaveChangesAsync(cancellationToken);
            throw new DbUpdateException("duplicate entry", new InvalidOperationException("1062"));
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
