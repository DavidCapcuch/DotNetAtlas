using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Admin correction — damage write-off, recount, transfer-out. Signed delta.
/// </summary>
/// <remarks>
/// Reducer: <c>OnHand += Delta</c>. Precondition (enforced in the command method):
/// <c>(OnHand + Delta) &gt;= 0</c> AND <c>(OnHand + Delta) - Reserved &gt;= 0</c> —
/// cannot adjust stock below zero or below the currently-reserved amount.
/// </remarks>
public sealed record StockAdjustedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }

    public required int Delta { get; init; }

    public required string Reason { get; init; }

    public Guid? AdjustedByUserId { get; init; }
}
