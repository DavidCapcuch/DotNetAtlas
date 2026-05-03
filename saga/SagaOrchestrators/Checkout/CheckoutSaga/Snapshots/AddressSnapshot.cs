namespace SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;

/// <summary>
/// Snapshot of a postal address - the JSON shape persisted into
/// <c>CheckoutSagaState.ShippingAddressJson</c> / <c>BillingAddressJson</c>. Frozen at
/// checkout-initiation time and read on the M4 transition that publishes
/// <c>CreateOrderCommand</c> to populate Ordering's <c>OrderAddress</c>. PII per ADR-0011:
/// the address columns are nulled out on every terminal saga transition.
/// </summary>
/// <remarks>
/// Promoted from <c>BasketCheckoutInitiatedConsumer.AddressSnapshot</c> in M4 so the
/// orchestrator can deserialise the same shape the M3 consumer wrote.
/// </remarks>
internal sealed record AddressSnapshot(
    string Street1,
    string? Street2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);
