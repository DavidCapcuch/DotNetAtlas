using Platform.CQRS;

namespace Inventory.Application.StockItems.AdjustStock;

/// <summary>
/// Signed correction to <c>OnHand</c> (damage write-off, recount, transfer-out).
/// The HTTP admin endpoint lands in M7 (backed by <c>.Idempotency()</c> per
/// ADR-0013); this handler is callable now for tests + the future endpoint.
/// </summary>
public sealed class AdjustStockCommand : ICommand
{
    public required Guid ProductId { get; init; }

    /// <summary>Signed delta; must not be zero.</summary>
    public required int Delta { get; init; }

    public required string Reason { get; init; }

    public required Guid AdjustedByUserId { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public Guid? CorrelationId { get; init; }
}
