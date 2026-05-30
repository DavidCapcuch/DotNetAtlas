using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.Domain.StockItems.Events;

/// <summary>
/// Bootstraps a new stream when a product is first referenced by Inventory.
/// Emitted on <c>InitializeStockItemCommand</c>, triggered by the inbox consumer of
/// Catalog's <c>ProductCreatedEvent</c>.
/// </summary>
/// <remarks>
/// Event-sourced persistence model AND in-process domain event. Stored directly in
/// <c>inventory.stock_events</c>; the CLR simple name is the discriminator round-tripped
/// by <see cref="Inventory.Infrastructure.Persistence.EventStore.StockEventSerializer"/>.
/// </remarks>
public sealed record StockItemInitializedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
}
