using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the aggregate reaches <c>Completed</c> — the successful terminal (though saga-reversible
/// via refund) state. Co-raised with <see cref="PaymentCapturedDomainEvent"/> in v1.
/// </summary>
/// <remarks>
/// <b>No in-process handler today.</b> The external <c>PaymentCompletedEvent</c> Avro record is
/// produced by the Checkout saga (per <c>events-catalog.md § 2</c>), not by Payments. The
/// Payments-side aggregate raises this domain event purely as a signal of the FSM-terminal
/// transition; no <c>IDomainEventHandler&lt;PaymentCompletedDomainEvent&gt;</c> is registered. Do
/// NOT wire a handler here (e.g. an outbox publisher) without ADR alignment — the wire-event
/// ownership boundary is intentional and the Checkout saga is the authoritative producer.
/// </remarks>
public sealed record PaymentCompletedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Money Amount { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
