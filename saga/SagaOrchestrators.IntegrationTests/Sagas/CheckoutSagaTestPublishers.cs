using Basket.Sessions;
using Ordering.Orders;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;

namespace SagaOrchestrators.IntegrationTests.Sagas;

/// <summary>
/// Shared static builders for the BC Avro events the end-to-end integration tests publish to
/// drive the Checkout saga. Lives here (under <c>Sagas/</c>) rather than under <c>Common/</c>
/// so it's adjacent to its callers and obvious in code review that the helpers are test-only
/// synthetic event factories — they mirror what Basket / Ordering would actually produce,
/// populating only the fields the saga's consumer adapters read (the rest are filled to
/// satisfy the Avro schema with deterministic values).
/// </summary>
/// <remarks>
/// <see cref="CheckoutSagaIntegrationTests"/> still uses its own private builders to keep
/// that file's git-blame intact and its blast radius zero.
/// </remarks>
internal static class CheckoutSagaTestPublishers
{
    /// <summary>
    /// Builds the saga-initiator <see cref="BasketCheckoutInitiatedEvent"/> from a tuple list
    /// of product lines. Conventions: "SKU-TEST" SKUs, "USD" currency, Prague address,
    /// freshly-minted PaymentMethodId per call.
    /// </summary>
    public static BasketCheckoutInitiatedEvent BuildBasketCheckoutInitiatedEvent(
        Guid correlationId,
        Guid userId,
        IReadOnlyList<(Guid ProductId, int Quantity, decimal UnitPrice)> lines)
    {
        var items = lines
            .Select(line => new BasketCheckoutItem
            {
                ProductId = line.ProductId,
                Sku = "SKU-TEST",
                Name = "Test Product",
                UnitPriceAmount = line.UnitPrice.ToAvroDecimal(4),
                UnitPriceCurrency = "USD",
                Quantity = line.Quantity,
                LineTotal = (line.UnitPrice * line.Quantity).ToAvroDecimal(4)
            })
            .ToList<BasketCheckoutItem>();

        var totalAmount = lines.Sum(line => line.UnitPrice * line.Quantity);

        var address = BuildAddress();

        return new BasketCheckoutInitiatedEvent
        {
            OrderId = correlationId,
            UserId = userId,
            Items = items,
            TotalAmount = totalAmount.ToAvroDecimal(4),
            Currency = "USD",
            ShippingAddress = address,
            BillingAddress = address,
            PaymentMethodId = Guid.CreateVersion7(),
            InitiatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds a synthetic <see cref="OrderCreatedEvent"/>. The saga's <c>OrderCreatedConsumer</c>
    /// reads only <c>OrderId</c>, <c>CorrelationId</c>, and <c>CreatedAtUtc</c>; remaining fields
    /// are populated solely to satisfy the Avro schema.
    /// </summary>
    public static OrderCreatedEvent BuildOrderCreatedEvent(
        Guid correlationId,
        Guid buyerId,
        Guid orderId)
    {
        return new OrderCreatedEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            Items = new List<OrderItemCreated>(),
            TotalAmount = 0m.ToAvroDecimal(4),
            Currency = "USD",
            PaymentMethodId = Guid.CreateVersion7(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds the deterministic Prague-CZ address used as both shipping and billing in tests.
    /// The values returned here are the same strings <see cref="AddressValueWitnesses"/> exposes
    /// for wire-level PII audit assertions (ADR-0011). Don't change one without the other.
    /// </summary>
    public static CheckoutAddress BuildAddress() =>
        new()
        {
            Street1 = "123 Test Street",
            Street2 = null,
            City = "Prague",
            State = null,
            PostalCode = "11000",
            CountryCode = "CZ"
        };

    /// <summary>
    /// Wire-level audit-fidelity witnesses (ADR-0011): the deterministic UTF-8 string values
    /// the integration-test helpers plant into the saga's
    /// <c>ShippingAddressJson</c>/<c>BillingAddressJson</c> via <see cref="BuildAddress"/>.
    /// Avro's binary encoding writes field VALUES (not field NAMES) as length-prefixed UTF-8,
    /// so a saga-terminal event that accidentally re-emitted the saga-state address payload
    /// would surface these strings verbatim in the outbox row's <c>AvroPayload</c> bytes —
    /// see <c>AssertNoAddressValuesInPayload</c>.
    ///
    /// Excludes <c>"CZ"</c> (two-letter country code) because it collides with currency codes
    /// and other short tokens in the wire payload; the remaining three are distinctive enough
    /// to act as canaries while not producing false positives.
    /// </summary>
    public static readonly string[] AddressValueWitnesses =
    [
        "123 Test Street",
        "Prague",
        "11000"
    ];
}
