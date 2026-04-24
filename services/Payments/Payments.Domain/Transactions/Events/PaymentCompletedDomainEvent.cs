using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the aggregate reaches <c>Completed</c> — the successful terminal (though saga-reversible
/// via refund) state. Co-raised with <see cref="PaymentCapturedDomainEvent"/> in v1.
/// </summary>
public sealed record PaymentCompletedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required Money Amount { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}
