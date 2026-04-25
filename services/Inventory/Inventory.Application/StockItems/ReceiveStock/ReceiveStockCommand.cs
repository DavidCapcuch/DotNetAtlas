using Platform.CQRS;

namespace Inventory.Application.StockItems.ReceiveStock;

/// <summary>
/// Records an inbound stock movement (warehouse delivery, return re-shelved,
/// transfer-in). Admin HTTP endpoint lands in M7; for now the handler is
/// callable via DI from integration tests and from the future M7 endpoint.
/// </summary>
public sealed class ReceiveStockCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    /// <summary>
    /// Free-form source label — e.g. <c>receiving-dock</c>, <c>returns</c>,
    /// <c>transfer-in</c>. Validated at value-object construction inside the
    /// aggregate.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Ops user id from the admin JWT. Null for system-initiated receipts
    /// (e.g. automated replenishment integration).
    /// </summary>
    public Guid? ReceivedByUserId { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public Guid? CorrelationId { get; init; }
}
