using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.MarkDelivered</c>. Drives the external
/// <c>OrderDeliveredEvent</c> outbox publisher — Notifications emails the
/// buyer; the order lifecycle reaches its terminal happy state.
/// </summary>
public sealed record OrderDeliveredDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required DateTimeOffset DeliveredAtUtc { get; init; }
}
