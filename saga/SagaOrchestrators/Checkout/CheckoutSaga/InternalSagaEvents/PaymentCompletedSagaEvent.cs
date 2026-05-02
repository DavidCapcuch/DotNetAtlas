namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that the Payments BC (via PaymentProcessingSaga) captured
/// payment. Adapted from the external <c>Payments.Transactions.PaymentCompletedEvent</c> by the
/// M3 consumer adapter (named <c>PaymentCompletedCheckoutConsumer</c> to avoid collision with
/// PaymentProcessingSaga's own consumer for the same event). Consumed in <c>AwaitingPayment</c>
/// (transition to <c>AwaitingConfirmation</c> per docs/bc-design/checkout-saga.md § 4
/// transition table).
/// </summary>
public sealed record PaymentCompletedSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Payment transaction id captured into <c>CheckoutSagaState.PaymentTransactionId</c>;
    /// required for compensation refund.
    /// </summary>
    public required Guid PaymentTransactionId { get; init; }

    /// <summary>
    /// Captured amount.
    /// </summary>
    public required decimal Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// UTC timestamp when Payments captured the payment.
    /// </summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }
}
