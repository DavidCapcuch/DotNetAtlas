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
/// String values are wire-protocol values. Every code is persisted to
/// <c>PaymentProcessingSagaState.ErrorCode</c>. Per ADR-0026 the sub-saga no
/// longer publishes payment-state events — the Payments service owns the
/// terminal <c>PaymentFailedEvent</c> — so every code below stays internal to
/// the saga. Treat them as stable contracts.
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
    /// The Checkout saga's capture-approval / abort signal did not arrive
    /// within the saga's <c>CaptureApprovalTimeout</c> budget (ADR-0026
    /// wait-state). Drives the void-payment path so the dangling
    /// authorization is released.
    /// </summary>
    public const string CaptureApprovalTimeout = "CAPTURE_APPROVAL_TIMEOUT";

    /// <summary>
    /// <c>PaymentCapturedEvent</c> / <c>PaymentCaptureFailedEvent</c> did
    /// not arrive within the saga's <c>CaptureTimeout</c> budget. Triggers
    /// a void-payment compensation.
    /// </summary>
    public const string CaptureTimeout = "CAPTURE_TIMEOUT";

    /// <summary>
    /// <c>PaymentVoidedEvent</c> did not arrive within the saga's
    /// <c>VoidTimeout</c> budget. Saga finalises in <c>VoidFailed</c>;
    /// manual intervention required.
    /// </summary>
    public const string VoidTimeout = "VOID_TIMEOUT";
}
