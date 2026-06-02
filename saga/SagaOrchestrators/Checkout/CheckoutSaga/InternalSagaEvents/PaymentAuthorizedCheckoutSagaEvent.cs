namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event signalling that the Payments BC authorized the payment. Adapted from the
/// external <c>Payments.Transactions.PaymentAuthorizedEvent</c> by <c>PaymentAuthorizedCheckoutConsumer</c>
/// (the <c>Checkout</c> suffix on both the consumer and this event disambiguates from
/// PaymentProcessingSaga's own <c>PaymentAuthorizedSagaEvent</c>, which carries the same intent in
/// a different bounded context). Per ADR-0026 the Checkout saga reacts to Payments' authorization
/// event directly: it drives order + reservation confirmation (the pre-pivot step), then approves
/// capture. Consumed in <c>AwaitingPaymentAuthorization</c> (transition to <c>AwaitingConfirmation</c>).
/// </summary>
public sealed record PaymentAuthorizedCheckoutSagaEvent
{
    /// <summary>
    /// Saga correlation id - matches <c>CheckoutSagaState.CorrelationId</c>.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Gateway authorization id. Carried for log/audit context; the sub-saga (not the Checkout
    /// saga) holds the authoritative copy used for the eventual capture/void.
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// UTC timestamp when Payments authorized the payment.
    /// </summary>
    public required DateTimeOffset AuthorizedAtUtc { get; init; }
}
