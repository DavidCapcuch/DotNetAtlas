using Platform.CQRS;

namespace Inventory.Application.StockItems.ReserveStock;

/// <summary>
/// Places a time-bounded hold of <see cref="Quantity"/> units against an order.
/// Issued by the Checkout saga (one per order line item). On success appends
/// <c>StockReservedDomainEvent</c> to the stream + emits the external Avro
/// <c>StockReservedEvent</c> to the outbox. On <c>Available &lt; Quantity</c>
/// no event is appended and an external <c>StockReservationFailedEvent</c> is
/// emitted to the outbox via the handler — never a throw
/// (inventory.md § 10.3 + DoD "InsufficientStock never throws").
/// </summary>
public sealed record ReserveStockCommand : ICommand
{
    /// <summary>Saga-supplied (GUIDv7). Unique per reservation.</summary>
    public required Guid ReservationId { get; init; }

    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    /// <summary>Saga correlation key; also Kafka message key on the response event.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Reservation time-to-live. Null = use the service-default TTL
    /// (15 minutes per inventory.md § 11). When provided, bounded
    /// 60s–3600s by the validator.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    public required DateTimeOffset OccurredOnUtc { get; init; }

    public Guid? CorrelationId { get; init; }
}
