namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the payment saga. The eShop's Checkout saga always
/// creates an Order before requesting payment, so <see cref="OrderId"/> — the saga correlation
/// key (ADR-0029) — is always present when the saga starts. The saga forwards it to the Payments
/// BC on the outbound <c>AuthorizePaymentCommand</c> as the aggregate's OrderId.
/// </summary>
public sealed record PaymentInitiatedSagaEvent
{
    /// <summary>
    /// Ordering aggregate id this payment is attached to — the saga correlation key (ADR-0029);
    /// equals <c>PaymentProcessingSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// User initiating the payment.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gateway-issued opaque payment-method token (1-64 chars).
    /// </summary>
    public required string PaymentMethodId { get; init; }

    /// <summary>
    /// Payment amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Idempotency key to prevent duplicate processing.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// UTC timestamp when payment was initiated.
    /// </summary>
    public required DateTime InitiatedAtUtc { get; init; }
}
