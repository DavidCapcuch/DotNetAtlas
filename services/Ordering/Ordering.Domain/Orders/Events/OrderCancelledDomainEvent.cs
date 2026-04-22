using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.Cancel</c>. Drives the external <c>OrderCancelledEvent</c>
/// outbox publisher — the Checkout saga inspects <see cref="AtStatus"/> to
/// dispatch the correct compensation pair (release stock at Inventory,
/// refund at Payments) per <c>ordering.md § 7</c>.
/// </summary>
public sealed record OrderCancelledDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }
    public required string AtStatus { get; init; }
    public required DateTimeOffset CancelledAtUtc { get; init; }
}
