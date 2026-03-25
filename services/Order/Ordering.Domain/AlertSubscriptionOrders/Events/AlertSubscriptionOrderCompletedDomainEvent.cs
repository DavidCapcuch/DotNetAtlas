using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.AlertSubscriptionOrders.Events;

/// <summary>
/// Raised when an alert subscription order transitions to the Completed status.
/// </summary>
public sealed record AlertSubscriptionOrderCompletedDomainEvent : DomainEvent
{
    public required Guid AlertSubscriptionOrderId { get; init; }
}
