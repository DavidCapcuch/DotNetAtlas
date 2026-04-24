using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised whenever the aggregate reaches <c>Failed</c> — regardless of whether the failure
/// originated in authorization or capture. The Checkout saga consumes the translated external
/// <c>PaymentFailedEvent</c> to drive its compensation branch.
/// </summary>
public sealed record PaymentFailedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required FailureInfo FailureInfo { get; init; }

    public required DateTimeOffset FailedAtUtc { get; init; }
}
