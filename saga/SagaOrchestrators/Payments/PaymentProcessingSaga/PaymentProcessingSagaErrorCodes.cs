namespace SagaOrchestrators.Payments.PaymentProcessingSaga;

/// <summary>
/// Saga-owned failure codes assigned by
/// <see cref="PaymentProcessingSagaOrchestrator"/> on schedule timeouts.
/// These constants are the source of truth for both the production
/// assignment and the assertions in saga tests.
/// </summary>
/// <remarks>
/// <para>
/// Codes propagated from upstream events (e.g. <c>CARD_DECLINED</c>,
/// <c>CAPTURE_FAILED</c>, <c>GATEWAY_TIMEOUT</c> originating in the
/// Payments BC's gateway adapter) are deliberately not listed here — the
/// saga forwards them unchanged.
/// </para>
/// <para>
/// String values are wire-protocol values: they flow into the persisted
/// saga state and into the <c>PaymentFailedEvent</c> emitted on the
/// <c>payments.transactions</c> topic on timeout paths. Treat them as
/// stable contracts.
/// </para>
/// </remarks>
public static class PaymentProcessingSagaErrorCodes
{
    /// <summary>
    /// <c>PaymentAuthorizedEvent</c> / <c>PaymentAuthorizationFailedEvent</c>
    /// did not arrive within the saga's <c>AuthorizationTimeout</c> budget.
    /// </summary>
    public const string AuthorizationTimeout = "AUTHORIZATION_TIMEOUT";

    /// <summary>
    /// <c>PaymentCapturedEvent</c> / <c>PaymentCaptureFailedEvent</c> did
    /// not arrive within the saga's <c>CaptureTimeout</c> budget. Triggers
    /// a void-payment compensation and is also forwarded into the
    /// <c>PaymentFailedEvent.ErrorCode</c> the saga emits.
    /// </summary>
    public const string CaptureTimeout = "CAPTURE_TIMEOUT";

    /// <summary>
    /// <c>PaymentVoidedEvent</c> did not arrive within the saga's
    /// <c>VoidTimeout</c> budget. Saga finalises in <c>VoidFailed</c>;
    /// manual intervention required.
    /// </summary>
    public const string VoidTimeout = "VOID_TIMEOUT";

    /// <summary>
    /// <c>PaymentRefundCompletedEvent</c> did not arrive within the saga's
    /// <c>RefundTimeout</c> budget. Saga finalises in <c>RefundFailed</c>;
    /// manual intervention required.
    /// </summary>
    public const string RefundTimeout = "REFUND_TIMEOUT";
}
