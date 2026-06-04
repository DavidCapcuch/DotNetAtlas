using Platform.SharedKernel.Base.DomainEvents;

namespace Ordering.Domain.Orders.Events;

/// <summary>
/// Raised by <c>Order.MarkPaymentCompleted</c>. Intra-service only — the saga
/// already observed Payments' <c>PaymentCompletedEvent</c> directly, so this
/// event is audit-only and does not produce an external message
/// (<c>ordering.md § 6</c> consumer table).
/// </summary>
public sealed record OrderPaymentCompletedDomainEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
    public required Guid PaymentTransactionId { get; init; }
}
