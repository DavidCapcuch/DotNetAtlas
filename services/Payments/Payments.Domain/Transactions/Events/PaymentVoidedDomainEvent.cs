using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the aggregate transitions to <c>Voided</c> as saga pre-capture compensation.
/// No money moved; gateway authorization released.
/// </summary>
public sealed record PaymentVoidedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required string GatewayTransactionId { get; init; }

    public required DateTimeOffset VoidedAtUtc { get; init; }
}
