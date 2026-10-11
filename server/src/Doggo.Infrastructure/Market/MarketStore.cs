using Doggo.Domain.Market;
using Doggo.Domain.News;
using Doggo.Infrastructure.Persistence;
using Doggo.Infrastructure.Vapor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Doggo.Infrastructure.Market;

public sealed record MarketSyncResult(int ItemsTouched, int SnapshotsAdded);

/// <summary>
/// Persists vapor market listings: upserts <see cref="MarketItem"/> catalog entries, stores
/// changed prices as <see cref="MarketPriceSnapshot"/> rows and folds them into
/// <see cref="MarketPriceDaily"/> OHLC rows on write, so trend charts never need a separate
/// aggregation pass. Daily rows are kept in the platform currency; snapshot rows keep the
/// currency they were captured with.
/// </summary>
public sealed partial class MarketStore(DoggoDbContext db, ILogger<MarketStore> logger)
{
    public const string CursorSource = "vapor-market";

    /// <summary>Steam currency ids → ISO codes; anything else is stored as CNY, the platform currency.</summary>
    private static readonly Dictionary<int, string> CurrencyCodes = new()
    {
        [1] = "USD",
        [3] = "GBP",
        [4] = "EUR",
        [5] = "RUB",
        [7] = "BRL",
        [8] = "JPY",
        [23] = "CNY"
    };

    public static GameKind MapGame(string appId) => appId switch
    {
        "730" => GameKind.Cs2,
        "570" => GameKind.Dota2,
        _ => GameKind.Shared
    };

    public static string MapCurrency(int? currencyId) =>
        currencyId is { } id && CurrencyCodes.TryGetValue(id, out var code) ? code : "CNY";

    public async Task<MarketSyncResult> ApplyListingsAsync(
        string appId,
        IReadOnlyList<VaporMarketListingView> listings,
        CancellationToken cancellationToken = default)
    {
        var game = MapGame(appId);
        var itemsTouched = 0;
        var snapshotsAdded = 0;

        foreach (var listing in listings)
        {
            if (string.IsNullOrWhiteSpace(listing.HashName))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var item = await UpsertItemAsync(game, listing, cancellationToken).ConfigureAwait(false);
            itemsTouched++;

            if (listing.TotalPrice is not > 0)
            {
                continue;
            }

            var added = await RecordPriceAsync(item, listing, cancellationToken).ConfigureAwait(false);
            snapshotsAdded += added ? 1 : 0;
        }

        await TouchCursorAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogApplied(logger, itemsTouched, snapshotsAdded, appId);
        return new MarketSyncResult(itemsTouched, snapshotsAdded);
    }

    /// <summary>
    /// Identity: (game, class id) for items that carry one, falling back to (game, market hash
    /// name). Aggregates without a class id merge into an existing class-id row for the same
    /// hash name so an item only ever has one row.
    /// </summary>
    private async Task<MarketItem> UpsertItemAsync(
        GameKind game,
        VaporMarketListingView listing,
        CancellationToken cancellationToken)
    {
        var classId = NormalizeClassId(listing.ClassId);

        var item = await FindItemAsync(game, classId, listing.HashName, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            item = new MarketItem
            {
                // ValueGeneratedNever: assign the key up front so several unsaved adds do not
                // collide on the temporary (default) key in the change tracker.
                Id = Guid.CreateVersion7(),
                ClassId = classId,
                MarketHashName = listing.HashName,
                ItemType = MarketItemType.Other,
                Game = game,
                IconUrl = string.Empty
            };
            db.MarketItems.Add(item);
            LogNewItem(logger, listing.HashName, game);
        }
        else if (item.ClassId.Length == 0 && classId.Length > 0)
        {
            item.ClassId = classId;
        }

        return item;
    }

    private async Task<MarketItem?> FindItemAsync(
        GameKind game,
        string classId,
        string hashName,
        CancellationToken cancellationToken)
    {
        // Items added earlier in this batch are not saved yet, so check the change tracker
        // before the database or the same batch would create duplicates.
        var local = db.MarketItems.Local.FirstOrDefault(i =>
            i.Game == game
            && ((classId.Length > 0 && i.ClassId == classId) || i.MarketHashName == hashName));
        if (local is not null)
        {
            return local;
        }

        if (classId.Length > 0)
        {
            var byClassId = await db.MarketItems
                .FirstOrDefaultAsync(i => i.Game == game && i.ClassId == classId, cancellationToken)
                .ConfigureAwait(false);
            if (byClassId is not null)
            {
                return byClassId;
            }
        }

        var byHash = await db.MarketItems
            .FirstOrDefaultAsync(i => i.Game == game && i.MarketHashName == hashName, cancellationToken)
            .ConfigureAwait(false);
        return byHash;
    }

    /// <summary>
    /// Stores a snapshot only when the price actually changed since the last capture for that
    /// (item, currency) pair; re-syncing an unchanged page then adds no rows.
    /// </summary>
    private async Task<bool> RecordPriceAsync(
        MarketItem item,
        VaporMarketListingView listing,
        CancellationToken cancellationToken)
    {
        var priceCents = (long)Math.Round(listing.TotalPrice!.Value * 100m, MidpointRounding.AwayFromZero);
        var currency = MapCurrency(listing.CurrencyId);
        var capturedAt = listing.FetchedAt.UtcDateTime;

        var latest = db.MarketPriceSnapshots.Local
            .Where(s => s.MarketItemId == item.Id && s.Currency == currency)
            .OrderByDescending(s => s.CapturedAtUtc)
            .FirstOrDefault();
        latest ??= await db.MarketPriceSnapshots
            .AsNoTracking()
            .Where(s => s.MarketItemId == item.Id && s.Currency == currency)
            .OrderByDescending(s => s.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (latest is { PriceCents: var lastPrice } && lastPrice == priceCents)
        {
            return false;
        }

        db.MarketPriceSnapshots.Add(new MarketPriceSnapshot
        {
            Id = Guid.CreateVersion7(),
            MarketItemId = item.Id,
            PriceCents = priceCents,
            Currency = currency,
            CapturedAtUtc = capturedAt
        });

        await FoldDailyAsync(item.Id, priceCents, capturedAt, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Folds a new price point into today's OHLC row, creating the row on the first price of the day.</summary>
    private async Task FoldDailyAsync(Guid itemId, long priceCents, DateTime capturedAtUtc, CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(capturedAtUtc);

        var daily = db.MarketPriceDailies.Local.FirstOrDefault(d => d.MarketItemId == itemId && d.Date == date);
        daily ??= await db.MarketPriceDailies
            .FirstOrDefaultAsync(d => d.MarketItemId == itemId && d.Date == date, cancellationToken)
            .ConfigureAwait(false);

        if (daily is null)
        {
            db.MarketPriceDailies.Add(new MarketPriceDaily
            {
                Id = Guid.CreateVersion7(),
                MarketItemId = itemId,
                Date = date,
                OpenCents = priceCents,
                HighCents = priceCents,
                LowCents = priceCents,
                CloseCents = priceCents
            });
            return;
        }

        if (priceCents > daily.HighCents)
        {
            daily.HighCents = priceCents;
        }

        if (priceCents < daily.LowCents)
        {
            daily.LowCents = priceCents;
        }

        daily.CloseCents = priceCents;
    }

    private async Task TouchCursorAsync(DateTimeOffset at, CancellationToken cancellationToken)
    {
        var cursor = await db.SyncCursors
            .FirstOrDefaultAsync(c => c.SourceName == CursorSource, cancellationToken)
            .ConfigureAwait(false);

        if (cursor is null)
        {
            db.SyncCursors.Add(new SyncCursor
            {
                Id = Guid.CreateVersion7(),
                SourceName = CursorSource,
                Cursor = at.ToString("O")
            });
        }
        else
        {
            cursor.Cursor = at.ToString("O");
        }
    }

    private static string NormalizeClassId(string? classId)
    {
        var value = classId?.Trim() ?? string.Empty;
        return value is "0" or "" ? string.Empty : value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "market sync applied {Items} items ({Snapshots} new snapshots) for app {AppId}")]
    private static partial void LogApplied(ILogger logger, int items, int snapshots, string appId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "new market item: {HashName} (game {Game})")]
    private static partial void LogNewItem(ILogger logger, string hashName, GameKind game);
}
