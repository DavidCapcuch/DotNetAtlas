using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Payments.Domain.Transactions.Events;

/// <summary>
/// Raised when the gateway capture call succeeds. Immediately followed by a
/// <see cref="PaymentCompletedDomainEvent"/> (v1 auto-completion on capture per
/// <c>docs/bc-design/payments.md § 4</c>).
/// </summary>
public sealed record PaymentCapturedDomainEvent : DomainEvent
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid BuyerId { get; init; }

    public required Guid OrderId { get; init; }

    public required string GatewayTransactionId { get; init; }

    public required Money Amount { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }
}
