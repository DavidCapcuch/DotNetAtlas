using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the aggregate transitions to <c>Refunded</c> as saga cancel-post-capture
/// compensation.
/// </summary>
public sealed record PaymentRefundedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required string GatewayTransactionId { get; init; }

    public required Money Amount { get; init; }

    public required string Reason { get; init; }

    public required DateTimeOffset RefundedAtUtc { get; init; }
}
