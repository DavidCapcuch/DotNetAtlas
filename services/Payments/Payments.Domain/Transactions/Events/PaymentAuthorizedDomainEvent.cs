using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the gateway authorization call succeeds and the aggregate transitions to
/// <c>Authorized</c>.
/// </summary>
public sealed record PaymentAuthorizedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required string GatewayTransactionId { get; init; }

    public required DateTimeOffset AuthorizedAtUtc { get; init; }
}
