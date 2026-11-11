using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Marketing;

/// <summary>Raised when the balance or request is unusable — maps to 400.</summary>
public sealed class CoinValidationException(string message) : Exception(message)
{
}

public sealed record CoinBalance(long Balance, DateTime CheckedAtUtc);

/// <summary>
/// Coin ledger: grants create expiring batches, spends consume batches FIFO by expiry,
/// refunds come back as a fresh non-expiring batch, and every movement writes a log row
/// with the balance after the change.
/// </summary>
public sealed partial class CoinService(DoggoDbContext db, ILogger<CoinService> logger)
{
    public async Task<CoinBalance> GetBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await db.UserCoins.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        return new CoinBalance(account?.Balance ?? 0, DateTime.UtcNow);
    }

    /// <summary>Tracks a grant without saving; the caller's single SaveChanges commits the
    /// coins together with its guard rows, so a lost guard race rolls the coins back too.</summary>
    public async Task<long> TrackGrantAsync(
        Guid userId, long amount, string sourceType, string? sourceRef, DateTime? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CoinValidationException("Coin amount must be positive.");
        }

        var account = await db.UserCoins.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (account is null)
        {
            account = new UserCoin { UserId = userId, Balance = 0, Version = 1 };
            db.UserCoins.Add(account);
        }

        account.Balance += amount;
        account.Version++;
        db.UserCoinBatches.Add(new UserCoinBatch
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OriginalAmount = amount,
            RemainingAmount = amount,
            SourceType = sourceType,
            SourceRef = sourceRef,
            ExpiresAtUtc = expiresAtUtc
        });
        db.UserCoinLogs.Add(new UserCoinLog
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Type = CoinLogType.Grant,
            Delta = amount,
            BalanceAfter = account.Balance,
            RefType = sourceType,
            RefId = sourceRef
        });
        return account.Balance;
    }

    /// <summary>Adds coins as one batch; expiry is optional. Concurrent credits re-read the
    /// balance on conflict so a grant is never lost.</summary>
    public async Task<long> GrantAsync(
        Guid userId, long amount, string sourceType, string? sourceRef, DateTime? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        // Two concurrent grants read the same balance; the version token makes the second
        // write fail so a credit is not lost. A create race loses to the unique user index
        // and retries against the winner's account.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var balance = await TrackGrantAsync(userId, amount, sourceType, sourceRef, expiresAtUtc, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                LogGranted(userId, amount, balance, sourceType);
                return balance;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                // Lost a create race to the unique user index; retry against the winner's
                // account. A store error that is not that race keeps throwing.
                if (!await db.UserCoins.AsNoTracking().AnyAsync(c => c.UserId == userId, cancellationToken))
                {
                    throw;
                }

                db.ChangeTracker.Clear();
            }
        }

        throw new CoinValidationException("The coin balance changed, please retry.");
    }

    /// <summary>
    /// Spends coins FIFO: earliest-expiring remaining batches are consumed first. The whole
    /// spend is rejected when the balance is insufficient; nothing is deducted partially.
    /// A concurrent spend fails the stale write, and the retry re-reads the balance and
    /// batches so two spends can never both succeed against one balance.
    /// </summary>
    public async Task<long> SpendAsync(Guid userId, long amount, string refType, string? refId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CoinValidationException("Coin amount must be positive.");
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var account = await db.UserCoins.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
                          ?? throw new CoinValidationException("No coin balance for this user.");

            var batches = await db.UserCoinBatches
                .Where(b => b.UserId == userId && b.RemainingAmount > 0)
                .ToListAsync(cancellationToken);
            // Null expiry (never expires) sorts last: spend the dying coins first.
            var ordered = batches
                .OrderBy(b => b.ExpiresAtUtc ?? DateTime.MaxValue)
                .ThenBy(b => b.CreatedAtUtc)
                .ToList();

            if (ordered.Sum(b => b.RemainingAmount) < amount)
            {
                throw new CoinValidationException("Insufficient coin balance.");
            }

            var left = amount;
            foreach (var batch in ordered)
            {
                if (left == 0)
                {
                    break;
                }

                var take = Math.Min(batch.RemainingAmount, left);
                batch.RemainingAmount -= take;
                left -= take;
            }

            account.Balance -= amount;
            account.Version++;
            db.UserCoinLogs.Add(new UserCoinLog
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Type = CoinLogType.Spend,
                Delta = -amount,
                BalanceAfter = account.Balance,
                RefType = refType,
                RefId = refId
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                LogSpent(userId, amount, account.Balance, refType);
                return account.Balance;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                db.ChangeTracker.Clear();
            }
        }

        throw new CoinValidationException("The coin balance changed, please retry.");
    }

    /// <summary>Puts coins back as a fresh non-expiring batch (refunds and corrections).</summary>
    public Task<long> RefundAsync(Guid userId, long amount, string refType, string? refId, CancellationToken cancellationToken = default) =>
        GrantAsync(userId, amount, "refund", refId, expiresAtUtc: null, cancellationToken);

    /// <summary>
    /// Zeroes out every expired batch and subtracts the remainder from the balance with an
    /// Expire log. Called by the worker sweep; returns the number of batches expired.
    /// </summary>
    public async Task<int> ExpireDueBatchesAsync(CancellationToken cancellationToken = default)
    {
        var due = await db.UserCoinBatches
            .Where(b => b.ExpiresAtUtc != null && b.ExpiresAtUtc <= DateTime.UtcNow && b.RemainingAmount > 0)
            .ToListAsync(cancellationToken);

        foreach (var group in due.GroupBy(b => b.UserId))
        {
            var account = await db.UserCoins.FirstOrDefaultAsync(c => c.UserId == group.Key, cancellationToken);
            var total = 0L;
            foreach (var batch in group)
            {
                total += batch.RemainingAmount;
                batch.RemainingAmount = 0;
            }

            if (account is null || total == 0)
            {
                continue;
            }

            account.Balance = Math.Max(0, account.Balance - total);
            account.Version++;
            db.UserCoinLogs.Add(new UserCoinLog
            {
                Id = Guid.CreateVersion7(),
                UserId = group.Key,
                Type = CoinLogType.Expire,
                Delta = -total,
                BalanceAfter = account.Balance,
                RefType = "expiry"
            });
            LogExpired(group.Key, total);
        }

        if (due.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A concurrent grant/spend moved the balance mid-sweep; drop the stale writes
                // — the batches stay due — and let the next sweep expire them.
                db.ChangeTracker.Clear();
            }
        }

        return due.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} granted {Amount} coins ({SourceType}), balance {Balance}")]
    private partial void LogGranted(Guid userId, long amount, long balance, string sourceType);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} spent {Amount} coins ({RefType}), balance {Balance}")]
    private partial void LogSpent(Guid userId, long amount, long balance, string refType);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} had {Amount} coins expire")]
    private partial void LogExpired(Guid userId, long amount);
}
