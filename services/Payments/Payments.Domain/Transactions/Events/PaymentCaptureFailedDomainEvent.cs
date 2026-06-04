using Payments.Domain.Transactions.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the gateway capture call fails after a successful authorization (rare). The
/// aggregate transitions to <c>Failed</c>; co-raised with <see cref="PaymentFailedDomainEvent"/>.
/// </summary>
public sealed record PaymentCaptureFailedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>
    /// Gateway-issued authorization token from the prior <c>Authorize</c> call. Always non-null
    /// because <c>MarkCaptureFailed</c> is only valid from <c>Authorized</c>, where the
    /// aggregate has already accepted a <c>GatewayTransactionId</c>. Outbox publishers map this
    /// onto <c>PaymentCaptureFailedEvent.AuthorizationId</c>.
    /// </summary>
    public required string GatewayTransactionId { get; init; }

    public required FailureInfo FailureInfo { get; init; }
}
