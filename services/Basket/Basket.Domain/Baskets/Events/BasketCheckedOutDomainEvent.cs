using Basket.Domain.Baskets.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.Events;

/// <summary>
/// In-process event — raised by <c>Basket.Checkout</c>. Carries the full basket
/// snapshot so the in-process outbox-publisher handler can map
/// to the external <c>BasketCheckoutInitiatedEvent</c> Avro record without
/// re-reading the aggregate.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ShippingAddress"/>, <see cref="BillingAddress"/>, and
/// <see cref="PaymentMethodId"/> are <b>courier fields</b> — Basket does not
/// own or validate addresses or payment methods beyond basic shape
/// ([ADR-0005](../../../../docs/adr/0005-customer-data-in-ordering.md)). They
/// ride the domain event solely so the outbox-publisher handler can stamp them
/// onto the external Avro event without knowing about the command boundary.
/// Ordering re-snapshots the addresses onto its own <c>Order</c> aggregate.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Courier field — shipping address supplied on the command. Basket does not
    /// persist or validate beyond <see cref="Address"/>'s basic shape checks.
    /// </summary>
    public required Address ShippingAddress { get; init; }

    /// <summary>
    /// Courier field — billing address supplied on the command. May equal
    /// <see cref="ShippingAddress"/>.
    /// </summary>
    public required Address BillingAddress { get; init; }

    /// <summary>
    /// Courier field — saved-payment-method reference owned by the Payments service.
    /// Basket only verifies it is non-empty; Payments validates on capture.
    /// </summary>
    public required Guid PaymentMethodId { get; init; }
}
