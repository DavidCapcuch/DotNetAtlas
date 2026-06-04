using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised when <c>Order.CreateFromBasket</c> succeeds. Drives the external
/// <c>OrderCreatedEvent</c> outbox publisher which emits to the
/// <c>ordering.orders</c> topic — the Checkout saga starts its instance on
/// that external event (<c>ordering.md § 6</c>).
/// </summary>
public sealed record OrderCreatedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public required IReadOnlyCollection<OrderCreatedDomainEventItem> Items { get; init; }
    public required Address ShippingAddress { get; init; }
    public required Address BillingAddress { get; init; }
    public required Money Total { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>
/// Flat line-level snapshot inside <see cref="OrderCreatedDomainEvent"/> —
/// kept primitive so the event serializes cleanly via the outbox publisher
/// without re-projecting strongly-typed <see cref="Money"/> per-item.
/// Per invariant I-9 the order is single-currency; the currency travels on
/// the parent event's <see cref="OrderCreatedDomainEvent.Total"/> rather
/// than on each item.
/// </summary>
public sealed record OrderCreatedDomainEventItem(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount,
    decimal LineTotalAmount);
