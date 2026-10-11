namespace Doggo.Domain.Cases;

/// <summary>
/// Seat lifecycle for batch opening, mirroring the legacy chain
/// 售卖中→锁定→已付款→开箱中→已开箱→获取库存→完成库存→交易中→完成.
/// Strictly linear; the only back edges are a locked seat returning to sale when its
/// order is cancelled, and a failed opening dispatch returning the seat to Paid for retry.
/// </summary>
public static class SeatStateMachine
{
    private static readonly IReadOnlyDictionary<SeatStatus, SeatStatus[]> Map =
        new Dictionary<SeatStatus, SeatStatus[]>
        {
            [SeatStatus.OnSale] = [SeatStatus.Locked],
            [SeatStatus.Locked] = [SeatStatus.OnSale, SeatStatus.Paid],
            [SeatStatus.Paid] = [SeatStatus.Opening],
            [SeatStatus.Opening] = [SeatStatus.Paid, SeatStatus.Opened],
            [SeatStatus.Opened] = [SeatStatus.FetchingStock],
            [SeatStatus.FetchingStock] = [SeatStatus.StockReady],
            [SeatStatus.StockReady] = [SeatStatus.Trading],
            [SeatStatus.Trading] = [SeatStatus.Completed]
        };

    public static bool CanTransition(SeatStatus from, SeatStatus to) =>
        Map.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void RequireTransition(SeatStatus from, SeatStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal seat transition {from} → {to}.");
        }
    }

    public static bool IsTerminal(SeatStatus status) => status == SeatStatus.Completed;
}
