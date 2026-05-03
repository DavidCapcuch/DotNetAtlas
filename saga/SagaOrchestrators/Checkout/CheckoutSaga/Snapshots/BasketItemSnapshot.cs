namespace SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;

/// <summary>
/// Snapshot of a single basket line item - the JSON shape persisted into
/// <c>CheckoutSagaState.BasketSnapshotJson</c>. Frozen at checkout-initiation time and read
/// during fan-out (per docs/bc-design/checkout-saga.md § 5.2 algorithm) to drive the
/// per-product <c>ReserveStockCommand</c> emission.
/// </summary>
/// <remarks>
/// Promoted from <c>BasketCheckoutInitiatedConsumer.BasketItemSnapshot</c> in M4 so the
/// orchestrator can deserialize the same shape the M3 consumer wrote.
/// </remarks>
internal sealed record BasketItemSnapshot(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    decimal UnitPriceAmount,
    string UnitPriceCurrency,
    decimal LineTotal);
