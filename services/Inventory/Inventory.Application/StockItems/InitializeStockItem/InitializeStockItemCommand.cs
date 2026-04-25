using Platform.CQRS;

namespace Inventory.Application.StockItems.InitializeStockItem;

/// <summary>
/// Creates a fresh event stream for a product. Issued by the Inventory Kafka
/// consumer (M5) translating Catalog's <c>ProductCreatedEvent</c>. Idempotent:
/// a second invocation on an already-initialized stream is a no-op
/// (<see cref="FluentResults.Result.Ok"/>, zero events appended).
/// </summary>
/// <remarks>
/// The aggregate throws <c>DataIntegrityException</c> on re-initialization, so
/// this handler guards against it explicitly by checking <c>Version</c> before
/// issuing <c>Initialize</c>. Duplicate delivery from Catalog is the common
/// case; it must NOT poison the consumer.
/// </remarks>
public sealed class InitializeStockItemCommand : ICommand
{
    public required Guid ProductId { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>
    /// Correlation id carried through to <c>stock_events.correlation_id</c>
    /// (ADR-0008). Null when the command was issued internally (not from a
    /// saga header).
    /// </summary>
    public Guid? CorrelationId { get; init; }
}
