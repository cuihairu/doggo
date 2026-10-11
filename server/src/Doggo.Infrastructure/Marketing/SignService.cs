using Doggo.Domain.Orders;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Marketing;

/// <summary>Raised when a sign or makeup request is unusable — maps to 400.</summary>
public sealed class SignValidationException(string message) : Exception(message)
{
}

public sealed record SignDay(DateOnly Date, bool Signed, long CoinReward, string? MakeupOrderNo);

public sealed record SignCycleView(
    DateOnly CycleId,
    IReadOnlyList<SignDay> Days,
    bool TodaySigned,
    long TotalReward);

public sealed record SignResult(DateOnly CycleId, DateOnly SignedDate, long CoinReward, long CycleTotalReward);

/// <summary>
/// Daily sign-in over natural-week cycles (Monday-based). Each day credits a fixed reward;
/// a missed day of the current week can be filled later with a completed order number as
/// the make-up proof, one order per day.
/// </summary>
public sealed partial class SignService(DoggoDbContext db, CoinService coins, IOptions<SignOptions> options, ILogger<SignService> logger)
{
    public async Task<SignResult> SignTodayAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await SignAsync(userId, today, makeupOrderNo: null, cancellationToken);
    }

    /// <summary>Signs or makes up a day. Today signs directly; a past day of the current
    /// week needs a completed order number that has not been used for makeup before.</summary>
    public async Task<SignResult> SignAsync(Guid userId, DateOnly date, string? makeupOrderNo, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cycleId = StartOfWeek(today);
        if (date > today)
        {
            throw new SignValidationException("You cannot sign a future day.");
        }
        if (date < cycleId)
        {
            throw new SignValidationException("Makeup only covers days of the current week.");
        }

        var signed = await db.SignRecords.FirstOrDefaultAsync(s => s.UserId == userId && s.SignedDate == date, cancellationToken);
        if (signed is not null)
        {
            throw new SignValidationException("This day is already signed.");
        }

        var isMakeup = date != today;
        if (isMakeup)
        {
            if (string.IsNullOrWhiteSpace(makeupOrderNo))
            {
                throw new SignValidationException("A completed order number is required to make up a day.");
            }

            var order = await db.UserOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderNo == makeupOrderNo, cancellationToken)
                ?? throw new SignValidationException("This order number does not exist.");
            if (order.UserId != userId)
            {
                throw new SignValidationException("This order does not belong to you.");
            }
            if (order.Status != OrderStatus.Completed)
            {
                throw new SignValidationException("Only completed orders can make up a day.");
            }

            var reused = await db.SignRecords.AnyAsync(
                s => s.MakeupOrderNo == makeupOrderNo, cancellationToken);
            if (reused)
            {
                throw new SignValidationException("This order was already used for a makeup sign.");
            }
        }

        var reward = options.Value.CoinReward;
        var balance = await coins.GrantAsync(userId, reward, isMakeup ? "sign_makeup" : "sign", date.ToString("O"), null, cancellationToken);
        var record = new SignRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CycleId = cycleId,
            SignedDate = date,
            CoinReward = reward,
            MakeupOrderNo = isMakeup ? makeupOrderNo : null
        };
        db.SignRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        var cycleTotal = await db.SignRecords
            .Where(s => s.UserId == userId && s.CycleId == cycleId)
            .SumAsync(s => s.CoinReward, cancellationToken);
        if (isMakeup)
        {
            LogMadeUp(date, userId, makeupOrderNo!);
        }
        else
        {
            LogSigned(date, userId);
        }

        return new SignResult(cycleId, date, reward, cycleTotal);
    }

    /// <summary>The current week's board: every day, signed or not, with today flagged.</summary>
    public async Task<SignCycleView> GetCycleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cycleId = StartOfWeek(today);
        var records = await db.SignRecords.AsNoTracking()
            .Where(s => s.UserId == userId && s.CycleId == cycleId)
            .ToListAsync(cancellationToken);

        var days = new List<SignDay>();
        for (var day = cycleId; day <= today; day = day.AddDays(1))
        {
            var record = records.FirstOrDefault(s => s.SignedDate == day);
            days.Add(new SignDay(day, record is not null, record?.CoinReward ?? 0, record?.MakeupOrderNo));
        }

        return new SignCycleView(cycleId, days, records.Any(s => s.SignedDate == today), records.Sum(s => s.CoinReward));
    }

    private static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} signed {Date}")]
    private partial void LogSigned(DateOnly date, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} made up {Date} with order {OrderNo}")]
    private partial void LogMadeUp(DateOnly date, Guid userId, string orderNo);
}
