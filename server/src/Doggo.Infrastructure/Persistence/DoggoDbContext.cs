using Microsoft.EntityFrameworkCore;

namespace Doggo.Infrastructure.Persistence;

/// <summary>
/// Application database context. Entities are registered here as domains land (see docs/design/data-model.md).
/// </summary>
public class DoggoDbContext(DbContextOptions<DoggoDbContext> options) : DbContext(options);
