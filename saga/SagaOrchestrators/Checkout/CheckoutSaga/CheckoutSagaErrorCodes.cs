namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// Saga-owned failure codes assigned by <see cref="CheckoutSagaOrchestrator"/>
/// on internal decisions (schedule timeouts and the saga's own categorisation
/// of stock-failed). These constants are the source of truth for both the
/// production assignment and the assertions in saga tests.
/// </summary>
/// <remarks>
/// <para>
/// Codes propagated from upstream bounded contexts (Payments BC's
/// <c>PaymentFailedEvent.ErrorCode</c>, Ordering BC's
/// <c>OrderFailedEvent.ErrorCode</c>) are deliberately not listed here — the
/// saga is a consumer of those vocabularies, not the owner, and reaching
/// across BC boundaries to share a constant would be heavier than warranted.
/// The Avro schemas type those fields as <c>string</c> on purpose
/// (extensible vocabulary).
/// </para>
/// <para>
/// String values are wire-protocol values written to <c>error_code</c> on
/// <c>saga.CheckoutSagaState</c> and surfaced on emitted
/// <c>CheckoutFailedEvent</c> / <c>CheckoutStuckEvent</c>. Treat them as
/// stable contracts — changing a value is a wire breakage, not a refactor.
/// </para>
/// </remarks>
public static class CheckoutSagaErrorCodes
{
    /// <summary>
    /// Stock reservation request was rejected because available inventory is
    /// less than requested. Assigned when <c>StockReservationFailedSagaEvent</c>
    /// is observed; overrides any inbound <c>ErrorCode</c> on the message.
    /// </summary>
    public const string StockUnavailable = "STOCK_UNAVAILABLE";

    /// <summary>
    /// Not all <c>StockReservedEvent</c>s arrived within the saga's
    /// <c>StockReservationTimeout</c> budget.
    /// </summary>
    public const string StockTimeout = "STOCK_TIMEOUT";

    /// <summary>
    /// <c>OrderCreatedEvent</c> did not arrive within the saga's
    /// <c>OrderCreationTimeout</c> budget.
    /// </summary>
    public const string OrderCreationTimeout = "ORDER_CREATION_TIMEOUT";

    /// <summary>
    /// <c>PaymentCompletedEvent</c> did not arrive within the saga's
    /// <c>PaymentTimeout</c> budget.
    /// </summary>
    public const string PaymentTimeout = "PAYMENT_TIMEOUT";

    /// <summary>
    /// <c>OrderConfirmedEvent</c> did not arrive within the saga's
    /// <c>OrderConfirmationTimeout</c> budget.
    /// </summary>
    public const string ConfirmationTimeout = "CONFIRMATION_TIMEOUT";

    /// <summary>
    /// A compensation (stock release or payment refund) did not complete
    /// within its timeout budget. Also used as the fallback value on
    /// <c>CheckoutStuckEvent.ErrorCode</c> when no specific code was assigned.
    /// </summary>
    public const string CompensationTimeout = "COMPENSATION_TIMEOUT";
}
