namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the payment saga.
/// This is a 'dumb' payment event - it knows nothing about business context (subscription, order, etc.).
/// </summary>
public sealed record PaymentInitiatedSagaEvent
{
    /// <summary>
    /// Correlation ID shared across the entire business flow.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the payment.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public required Guid PaymentMethodId { get; init; }

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
