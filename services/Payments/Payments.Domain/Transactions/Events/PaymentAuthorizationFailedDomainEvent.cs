using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the gateway authorization call fails and the aggregate transitions to
/// <c>Failed</c>. Co-raised with <see cref="PaymentFailedDomainEvent"/> so downstream can react
/// to either the specific auth-failure signal or the generic terminal-failure signal.
/// </summary>
public sealed record PaymentAuthorizationFailedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required FailureInfo FailureInfo { get; init; }
}
