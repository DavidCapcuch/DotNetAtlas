using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

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

    /// <summary>
    /// Authorized amount + currency snapshotted from the aggregate so outbox publishers can
    /// populate <c>PaymentAuthorizedEvent.Amount</c> + <c>Currency</c> without looking the
    /// aggregate up via the EF change-tracker.
    /// </summary>
    public required Money Amount { get; init; }

    public required DateTimeOffset AuthorizedAtUtc { get; init; }
}
