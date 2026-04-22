using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.Checkout</c>. Carries the full basket
/// snapshot so the in-process outbox-publisher handler (milestone M4) can map
/// to the external <c>BasketCheckoutInitiatedEvent</c> Avro record without
/// re-reading the aggregate.
/// </summary>
public sealed record BasketCheckedOutDomainEvent : DomainEvent
{
    public required Guid UserId { get; init; }

    /// <summary>
    /// Correlation identifier supplied by the caller (typically <c>Guid.CreateVersion7()</c>
    /// from the API layer). Becomes the Checkout Saga's correlation id.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Full snapshot of the basket at the moment of checkout.
    /// </summary>
    public required BasketSnapshot Snapshot { get; init; }
}
