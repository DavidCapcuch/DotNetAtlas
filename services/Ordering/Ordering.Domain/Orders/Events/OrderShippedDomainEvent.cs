using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.MarkShipped</c>. Drives the external <c>OrderShippedEvent</c>
/// outbox publisher — Notifications emails the buyer with tracking info.
/// </summary>
public sealed record OrderShippedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Carrier { get; init; }
    public required string TrackingNumber { get; init; }
    public required DateTimeOffset ShippedAtUtc { get; init; }
}
