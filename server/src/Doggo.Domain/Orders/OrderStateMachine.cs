namespace Doggo.Domain.Orders;

/// <summary>
/// Allowed order transitions. Everything else is a caller bug, so transitions are checked
/// through this single table instead of ad-hoc status switches.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.PendingSettlement, OrderStatus.Cancelled],
        [OrderStatus.PendingSettlement] = [OrderStatus.PendingRetrieval, OrderStatus.Refunded],
        [OrderStatus.PendingRetrieval] = [OrderStatus.Retrieving, OrderStatus.Refunded],
        [OrderStatus.Retrieving] = [OrderStatus.Completed, OrderStatus.PendingRetrieval, OrderStatus.Refunded]
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void RequireTransition(OrderStatus from, OrderStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Order cannot move from {from} to {to}.");
        }
    }

    public static bool IsTerminal(OrderStatus status) => status switch
    {
        OrderStatus.Completed or OrderStatus.Cancelled or OrderStatus.Refunded => true,
        _ => false
    };
}
