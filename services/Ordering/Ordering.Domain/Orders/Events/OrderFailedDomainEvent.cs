using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.Fail</c>. Drives the external <c>OrderFailedEvent</c>
/// outbox publisher — Notifications renders the failure to the buyer; the
/// Checkout saga closes its instance and dispatches the appropriate
/// compensation pair per <see cref="AtStatus"/>.
/// </summary>
public sealed record OrderFailedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid CorrelationId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required string AtStatus { get; init; }
    public required DateTimeOffset FailedAtUtc { get; init; }
}
