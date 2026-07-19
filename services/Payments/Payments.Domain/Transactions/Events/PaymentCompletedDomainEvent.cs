using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the aggregate reaches <c>Completed</c> — the successful terminal (though saga-reversible
/// via refund) state. Co-raised with <see cref="PaymentCapturedDomainEvent"/> in v1.
/// </summary>
/// <remarks>
/// Per <b>ADR-0026</b> the Payments service owns all its lifecycle integration events including
/// the terminals. <see cref="Payments.Application.Outbox.PaymentCompletedOutboxPublisherDomainEventHandler"/>
/// fans this domain event out to the external <c>PaymentCompletedEvent</c> on
/// <c>payments.transactions</c> — symmetric with the Authorized / Captured / Voided / Refunded
/// publishers. Co-raised with <see cref="PaymentCapturedDomainEvent"/> on a successful capture;
/// PaymentProcessingSaga does not publish this event (it orchestrates only).
/// </remarks>
public sealed record PaymentCompletedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Money Amount { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
