using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.Confirm</c>. Drives the external <c>OrderConfirmedEvent</c>
/// outbox publisher — Notifications renders buyer-facing confirmation emails,
/// BFF invalidates order cache, the Checkout saga advances to complete, and
/// Invoicing reads the embedded summary to issue the invoice.
/// </summary>
/// <remarks>
/// As of Wave 1.5 / ADR-0020 this is a Summary Event: it carries the order's
/// full state at the confirmation transition (<see cref="Items"/>,
/// <see cref="Total"/>, <see cref="BillingAddress"/>) so downstream consumers
/// — particularly Invoicing under 10-year retention — can rebuild state
/// without an HTTP round-trip back to Ordering.
/// </remarks>
public sealed record OrderConfirmedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }

    /// <summary>
    /// Confirmed-order line snapshots — frozen per Order invariant I-2 once
    /// stock is reserved. Always at least one item per invariant I-7.
    /// </summary>
    public required IReadOnlyCollection<OrderItem> Items { get; init; }

    /// <summary>
    /// Order total and currency at confirmation time. <see cref="Money.Currency"/>
    /// covers the wire schema's <c>Currency</c> field; the mapper splits.
    /// </summary>
    public required Money Total { get; init; }

    /// <summary>
    /// Buyer's billing address snapshot. Required on the domain event because
    /// confirmed orders always have a billing address (set at
    /// <see cref="Order.CreateFromBasket"/>); the wire schema makes it nullable
    /// only for FORWARD_TRANSITIVE compatibility per ADR-0007 + ADR-0020.
    /// </summary>
    public required Address BillingAddress { get; init; }

    /// <summary>
    /// UTC timestamp of the Confirm transition. Carried explicitly (rather
    /// than reusing <see cref="OccurredOnUtc"/>) so the outbox publisher
    /// mapper can read a single saga-time field for the Avro
    /// <c>ConfirmedAtUtc</c> payload — symmetric with
    /// <see cref="OrderCreatedDomainEvent.CreatedAtUtc"/> and
    /// <see cref="OrderCancelledDomainEvent.CancelledAtUtc"/>.
    /// </summary>
    public required DateTimeOffset ConfirmedAtUtc { get; init; }
}
