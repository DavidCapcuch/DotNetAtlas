namespace SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;

/// <summary>
/// Snapshot of a postal address - the JSON shape persisted into
/// <c>CheckoutSagaState.ShippingAddressJson</c> / <c>BillingAddressJson</c>. Frozen at
/// checkout-initiation time and read on the transition that publishes
/// <c>CreateOrderCommand</c> to populate Ordering's <c>OrderAddress</c>. PII per ADR-0011:
/// the address columns are nulled out on every terminal saga transition.
/// </summary>
/// <remarks>
/// Same shape the <c>BasketCheckoutInitiatedConsumer</c> wrote so the orchestrator can
/// deserialise it.
/// </remarks>
internal sealed record AddressSnapshot(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);
