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

    /// <summary>Adds coins as one batch; expiry is optional.</summary>
    public async Task<long> GrantAsync(
        Guid userId, long amount, string sourceType, string? sourceRef, DateTime? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CoinValidationException("Coin amount must be positive.");
        }

        var account = await db.UserCoins.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (account is null)
        {
            account = new UserCoin { UserId = userId, Balance = 0 };
            db.UserCoins.Add(account);
        }

        account.Balance += amount;
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
        await db.SaveChangesAsync(cancellationToken);
        LogGranted(userId, amount, account.Balance, sourceType);
        return account.Balance;
    }

    /// <summary>
    /// Spends coins FIFO: earliest-expiring remaining batches are consumed first. The whole
    /// spend is rejected when the balance is insufficient; nothing is deducted partially.
    /// </summary>
    public async Task<long> SpendAsync(Guid userId, long amount, string refType, string? refId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            throw new CoinValidationException("Coin amount must be positive.");
        }

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
        await db.SaveChangesAsync(cancellationToken);
        LogSpent(userId, amount, account.Balance, refType);
        return account.Balance;
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
            await db.SaveChangesAsync(cancellationToken);
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
