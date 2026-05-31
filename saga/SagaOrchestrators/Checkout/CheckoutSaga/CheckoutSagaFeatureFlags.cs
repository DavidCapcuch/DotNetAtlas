namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// OpenFeature flag keys owned by the Checkout saga (ADR-0014).
/// </summary>
public static class CheckoutSagaFeatureFlags
{
    /// <summary>
    /// Topology-swap flag (ADR-0014 § Implementation Notes — flag #3). When <c>false</c> (default
    /// per ADR-0004), the saga runs <b>stock-then-payment</b>: after <c>OrderCreated</c> it fans out
    /// <c>ReserveStockCommand</c> per distinct ProductId and only requests payment once every
    /// reservation lands. When <c>true</c>, the experimental <b>payment-then-stock</b> branch is
    /// taken: the saga immediately publishes <c>RequestPaymentCommand</c> and transitions to
    /// <c>AwaitingPayment</c> without reserving stock first.
    ///
    /// The ON path is <b>intentionally not validated end-to-end in v1</b> — per ADR-0014 line 116
    /// the flag exists to demonstrate the gated branch without shipping an untested topology.
    /// Downstream <c>AwaitingPayment</c> handlers still assume stock has been reserved, so a
    /// happy-path completion under the ON branch is not yet supported.
    /// </summary>
    public const string PaymentThenStock = "checkout.payment-then-stock";
}
