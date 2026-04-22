using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.Confirm</c>. Drives the external <c>OrderConfirmedEvent</c>
/// outbox publisher — Notifications renders buyer-facing confirmation emails,
/// BFF invalidates order cache, the Checkout saga advances to complete.
/// </summary>
public sealed record OrderConfirmedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required Guid BuyerId { get; init; }
}
