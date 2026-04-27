namespace SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the payment saga. The eShop's Checkout saga always
/// creates an Order before requesting payment, so <see cref="OrderId"/> is always present
/// when the saga starts. The saga forwards it to the Payments BC on the outbound
/// <c>AuthorizePaymentCommand</c> for aggregate-side persistence + admin-debugging lookups;
/// downstream <c>Payments.*Event</c> emissions drop OrderId (cross-BC linkage stays
/// CorrelationId).
/// </summary>
public sealed record PaymentInitiatedSagaEvent
{
    /// <summary>
    /// Correlation ID shared across the entire business flow.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Ordering aggregate id this payment is attached to.
    /// </summary>
    public required Guid OrderId { get; init; }

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
