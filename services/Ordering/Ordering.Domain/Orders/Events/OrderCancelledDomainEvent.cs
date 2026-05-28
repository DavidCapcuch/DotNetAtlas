using Ordering.Domain.Orders.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.Cancel</c>. Drives the external <c>OrderCancelledEvent</c>
/// outbox publisher — the Checkout saga inspects <see cref="AtStatus"/> to
/// dispatch the correct compensation pair (release stock at Inventory,
/// refund at Payments) per <c>ordering.md § 7</c>; Invoicing's credit-note
/// projection captures the embedded summary fields for credit-note issuance.
/// </summary>
/// <remarks>
/// As of Wave 1.6 / ADR-0020 this is a Summary Event: it carries the order's
/// state at the cancellation transition (<see cref="Items"/>,
/// <see cref="Total"/>, <see cref="BillingAddress"/>) so downstream consumers
/// — particularly Invoicing under 10-year retention — can rebuild state
/// without an HTTP round-trip back to Ordering.
/// </remarks>
public sealed record OrderCancelledDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }
    public required string AtStatus { get; init; }
    public required DateTimeOffset CancelledAtUtc { get; init; }

    /// <summary>
    /// Cancelled-order line snapshots — frozen per Order invariant I-2.
    /// Always at least one item per invariant I-7.
    /// </summary>
    public required IReadOnlyCollection<OrderItem> Items { get; init; }

    /// <summary>
    /// Order total and currency at cancellation time. <see cref="Money.Currency"/>
    /// covers the wire schema's <c>Currency</c> field; the mapper splits.
    /// </summary>
    public required Money Total { get; init; }

    /// <summary>
    /// Buyer's billing address snapshot. Required on the domain event because
    /// orders always have a billing address (set at
    /// <see cref="Order.CreateFromBasket"/>); the wire schema makes it nullable
    /// only for FORWARD_TRANSITIVE compatibility per ADR-0020.
    /// </summary>
    public required Address BillingAddress { get; init; }
}
