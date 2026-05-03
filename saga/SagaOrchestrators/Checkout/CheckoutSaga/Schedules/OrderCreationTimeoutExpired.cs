namespace SagaOrchestrators.Checkout.CheckoutSaga.Schedules;

/// <summary>
/// Fires when <c>AwaitingOrderCreation</c> exceeds the configured
/// <c>Saga:CheckoutTimeouts:OrderCreationSeconds</c> budget (default 30s) per
/// docs/bc-design/checkout-saga.md § 7. Triggers the § 3 transition row that moves the
/// saga to <c>Failed</c> with <c>ErrorCode = ORDER_CREATION_TIMEOUT</c>.
/// </summary>
public sealed record OrderCreationTimeoutExpired
{
    /// <summary>
    /// Correlation ID linking to the saga.
    /// </summary>
    public Guid CorrelationId { get; init; }
}
