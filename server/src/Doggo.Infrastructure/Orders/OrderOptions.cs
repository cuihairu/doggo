namespace Doggo.Infrastructure.Orders;

public sealed class OrderOptions
{
    public const string SectionName = "Order";

    /// <summary>Unpaid orders are cancelled after this many seconds.</summary>
    public int PayTimeoutSeconds { get; set; } = 900;

    /// <summary>Stuck PendingSettlement/PendingRetrieval orders are refunded after this many minutes.</summary>
    public int SettlementStuckMinutes { get; set; } = 30;

    /// <summary>Stuck Retrieving orders roll back to PendingRetrieval after this many minutes.</summary>
    public int RetrievalStuckMinutes { get; set; } = 120;

    /// <summary>How often the timeout and reconciliation workers run.</summary>
    public int WorkerIntervalSeconds { get; set; } = 30;
}
