namespace SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

/// <summary>
/// Internal saga event that initiates the Checkout saga. Adapted from the external
/// <c>Basket.Sessions.BasketCheckoutInitiatedEvent</c> by the consumer adapter, which maps
/// the basket's pre-assigned <c>OrderId</c> onto <see cref="CorrelationId"/>. Consumed in the
/// <c>Initial</c> state (transition to <c>AwaitingOrderCreation</c> per
/// docs/bc-design/checkout-saga.md § 4 transition table). This is the saga's only initiator —
/// no <c>OnMissingInstance</c> policy is configured because the <c>Initially(...)</c> block
/// creates the instance.
/// </summary>
public sealed record BasketCheckoutInitiatedSagaEvent
{
    /// <summary>
    /// Saga correlation id - equals the basket's pre-assigned <c>OrderId</c> (UUID v7) per ADR-0029.
    /// Threads through every downstream command/event for the lifetime of the workflow.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Identifier of the user initiating checkout. Becomes Ordering's BuyerId.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Serialized snapshot of basket line items (ProductId, Sku, Name, Quantity, UnitPriceAmount,
    /// LineTotal). Stored as-is into <c>CheckoutSagaState.BasketSnapshotJson</c>; the snapshot is
    /// immutable for the saga's lifetime.
    /// </summary>
    public required string BasketSnapshotJson { get; init; }

    /// <summary>
    /// Sum of line totals as captured by Basket at checkout initiation.
    /// </summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>
    /// ISO 4217 currency code (e.g. "USD", "EUR").
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// Saved payment method id — still <c>Guid</c> because the Basket-side wire schema
    /// (<c>BasketCheckoutInitiatedEvent</c>) is unchanged. CheckoutSaga converts to a
    /// gateway-token string only when emitting the Payments-side <c>RequestPaymentCommand</c>
    /// (Payments-side schema changed; Basket + Ordering wire shapes deferred).
    /// </summary>
    public required Guid PaymentMethodId { get; init; }

    /// <summary>
    /// Serialized shipping address value object. Held only for the saga lifetime per ADR-0011 PII
    /// retention - nulled out on terminal transitions.
    /// </summary>
    public string? ShippingAddressJson { get; init; }

    /// <summary>
    /// Serialized billing address value object. Same retention rule as
    /// <see cref="ShippingAddressJson"/>.
    /// </summary>
    public string? BillingAddressJson { get; init; }

    /// <summary>
    /// UTC timestamp captured by Basket at checkout initiation.
    /// </summary>
    public required DateTimeOffset InitiatedAtUtc { get; init; }
}
