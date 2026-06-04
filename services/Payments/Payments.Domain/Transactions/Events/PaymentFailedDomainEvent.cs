using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised whenever the aggregate reaches <c>Failed</c> — regardless of whether the failure
/// originated in authorization or capture. The Checkout saga consumes the translated external
/// <c>PaymentFailedEvent</c> to drive its compensation branch.
/// </summary>
/// <remarks>
/// Per <b>ADR-0026</b> the Payments service owns all its lifecycle integration events including
/// the terminals. <see cref="Payments.Application.Outbox.PaymentFailedOutboxPublisherDomainEventHandler"/>
/// fans this domain event out to the external <c>PaymentFailedEvent</c> on
/// <c>payments.transactions</c> — symmetric with the AuthorizationFailed / CaptureFailed
/// publishers. Co-raised on both <see cref="PaymentAuthorizationFailedDomainEvent"/> and
/// <see cref="PaymentCaptureFailedDomainEvent"/>; the Checkout saga consumes the external event to
/// fast-fail on an authorization decline. PaymentProcessingSaga no longer publishes this event.
/// </remarks>
public sealed record PaymentFailedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required FailureInfo FailureInfo { get; init; }

    public required DateTimeOffset FailedAtUtc { get; init; }
}
