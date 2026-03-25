using Ordering.Domain.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.AlertSubscriptionOrders.Events;

/// <summary>
/// Raised when a new alert subscription purchase order is created.
/// </summary>
public sealed record AlertSubscriptionPurchaseOrderCreatedDomainEvent : DomainEvent
{
    public required Guid AlertSubscriptionOrderId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid PaymentMethodId { get; init; }
    public required AlertSubscriptionTier Tier { get; init; }
    public required int DurationDays { get; init; }
    public required Money Price { get; init; }
}
