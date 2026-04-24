using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when a <see cref="PaymentTransaction"/> is created. Consumed in-process by the outbox
/// publisher (M4) which translates it into the external <c>PaymentRequestedEvent</c> on
/// <c>payments.transactions</c>.
/// </summary>
public sealed record PaymentRequestedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Money Amount { get; init; }

    public required PaymentMethodId PaymentMethodId { get; init; }
}
