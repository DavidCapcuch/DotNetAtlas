using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.AlertSubscriptionOrders.Events;

/// <summary>
/// Raised when an alert subscription order transitions to the Failed status.
/// </summary>
public sealed record AlertSubscriptionOrderFailedDomainEvent : DomainEvent
{
    public required Guid AlertSubscriptionOrderId { get; init; }
}
