using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Bootstraps a new stream when a product is first referenced by Inventory.
/// Emitted on <c>InitializeStockItemCommand</c>, triggered by the inbox consumer of
/// Catalog's <c>ProductCreatedEvent</c>.
/// </summary>
/// <remarks>
/// Event-sourced persistence model AND in-process domain event. Suffix is
/// intentionally <c>Event</c> (not <c>DomainEvent</c>) because this record is stored
/// directly in <c>inventory.stock_events</c>. See
/// <c>docs/bc-design/inventory.md</c> § 5 for reducer semantics.
/// </remarks>
public sealed record StockItemInitializedEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
}
