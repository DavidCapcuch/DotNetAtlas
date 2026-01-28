namespace DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the payment saga.
/// This is a 'dumb' payment event - it knows nothing about business context (subscription, order, etc.).
/// </summary>
public sealed record PaymentInitiatedSagaEvent
{
    /// <summary>
    /// Correlation ID shared across the entire business flow.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// User initiating the payment.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// ID of the saved payment method to use.
    /// </summary>
    public Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Payment amount.
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key to prevent duplicate processing.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when payment was initiated.
    /// </summary>
    public DateTime InitiatedAtUtc { get; init; }
}
