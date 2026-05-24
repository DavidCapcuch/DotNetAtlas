using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised whenever the aggregate reaches <c>Failed</c> — regardless of whether the failure
/// originated in authorization or capture. The Checkout saga consumes the translated external
/// <c>PaymentFailedEvent</c> to drive its compensation branch.
/// </summary>
/// <remarks>
/// <b>No in-process handler today.</b> The external <c>PaymentFailedEvent</c> Avro record is
/// produced by the Checkout saga (per <c>events-catalog.md § 2</c>), not by Payments. The
/// Payments-side aggregate raises this domain event purely as a signal of the FSM-terminal
/// transition; no <c>IDomainEventHandler&lt;PaymentFailedDomainEvent&gt;</c> is registered. Do
/// NOT wire a handler here (e.g. an outbox publisher) without ADR alignment — the wire-event
/// ownership boundary is intentional and the Checkout saga is the authoritative producer.
/// </remarks>
public sealed record PaymentFailedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required FailureInfo FailureInfo { get; init; }

    public required DateTimeOffset FailedAtUtc { get; init; }
}
