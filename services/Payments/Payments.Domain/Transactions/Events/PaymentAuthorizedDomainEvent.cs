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

    /// <summary>
    /// UTC instant at which the gateway-issued authorization expires. Sourced from the gateway
    /// response (carried through <c>AuthorizeResponse.ExpiresAtUtc</c>); v1 stub returns
    /// <c>AuthorizedAtUtc + 7 days</c>. Surfaced on the wire <c>PaymentAuthorizedEvent</c> so
    /// downstream consumers (capture-deadline alerting) see a truthful value rather than a
    /// synthesized placeholder (H-6 closeout).
    /// </summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
