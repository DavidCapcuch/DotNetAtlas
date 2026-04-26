using Inventory.Application.StockItems.Common;
using Platform.CQRS;

namespace Inventory.Application.StockItems.AdjustStock;

/// <summary>
/// Signed correction to <c>OnHand</c> (damage write-off, recount, transfer-out).
/// Returns the post-mutation
/// <see cref="StockLevelResponse"/> so the admin caller (HTTP endpoint backed
/// by <c>.Idempotency()</c> per ADR-0013) can render the updated figures.
/// </summary>
public sealed class AdjustStockCommand : ICommand<StockLevelResponse>
{
    public required Guid ProductId { get; init; }

    /// <summary>Signed delta; must not be zero.</summary>
    public required int Delta { get; init; }

    public required string Reason { get; init; }

    public required Guid AdjustedByUserId { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public Guid? CorrelationId { get; init; }
}
