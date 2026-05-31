using System.Text;
using System.Text.Json;
using Basket.Sessions;
using Checkout.Sagas;
using Inventory.Reservations;
using Microsoft.EntityFrameworkCore;
using Ordering.Orders;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;
using SagaOrchestrators.IntegrationTests.Common;

namespace SagaOrchestrators.IntegrationTests.Sagas;

/// <summary>
/// End-to-end integration tests for the Checkout saga happy path. Drives the saga from
/// <see cref="CheckoutSagaOrchestrator.AwaitingOrderCreation"/> all the way to the
/// <see cref="CheckoutSagaOrchestrator.Confirmed"/> terminal via real Kafka
/// (BC <c>basket.sessions</c> / <c>ordering.orders</c> / <c>inventory.reservations</c> /
/// <c>payments.payments</c> response events) against the real EF Core saga repository in
/// Postgres (Testcontainers via <see cref="SagaIntegrationTestFixture"/>); asserts on outbox
/// commands and on the wire-shape of the published <see cref="CheckoutCompletedEvent"/>.
/// </summary>
/// <remarks>
/// Per the orchestrator's <c>SetCompletedWhenFinalized()</c> + per-terminal <c>.Finalize()</c>,
/// the saga row is removed by the EF saga repository on terminal. The PII null-out rule from
/// ADR-0011 is asserted at two layers — (a) the saga row is gone after terminal (verified via
/// <see cref="SagaStateMonitor{TSaga,TSagaState}.WaitForFinalizedAsync"/>); (b) the saga-terminal
/// Avro event payload bytes contain none of the deterministic address VALUES the test seeded
/// into the saga state on initiation (verified via UTF-8 byte scan against
/// <see cref="CheckoutSagaTestPublishers.AddressValueWitnesses"/>). Avro binary encoding does
/// NOT include field names — only values are length-prefixed UTF-8 — so a value-witness scan is
/// the correct shape for wire-level audit-fidelity.
/// The orchestrator's in-saga <c>NullOutAddresses</c> step has unit-level coverage already;
/// this test pins the persistence + wire boundary at the integration level.
/// </remarks>
[Collection(nameof(SagaTestCollection))]
public class CheckoutSagaEndToEndIntegrationTests : BaseSagaIntegrationTest
{
    // Bias to 15s (vs M6's 10s) for the longer M9 chains: BasketCheckoutInitiated → OrderCreated
    // → 3× StockReserved → PaymentCompleted → OrderConfirmed crosses 5 saga consume cycles, each
    // doing one DB write + one Kafka publish. M5/M6 race pre-mortems show ~5–10s p99 for shorter
    // chains; the extra 5s headroom keeps genuine deadlocks catchable without flake.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Schema-level audit-fidelity allow-list (ADR-0011): the field NAMES that may not appear on
    /// any saga-terminal Avro record's public properties. Catches a future schema regression that
    /// adds an address-shaped field at the top level. Wire-level audit is via
    /// <see cref="CheckoutSagaTestPublishers.AddressValueWitnesses"/>; the two checks are
    /// complementary, not redundant — schema reflection inspects record shape, value scan
    /// inspects what actually got serialised.
    /// </summary>
    private static readonly string[] AddressFieldNames =
    [
        "Street1",
        "Street2",
        "PostalCode",
        "ShippingAddress",
        "BillingAddress"
    ];

    private SagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState> SagaStateMonitor { get; }

    public CheckoutSagaEndToEndIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState>();
    }

    [Fact]
    public async Task HappyPath_ThreeProducts_ReachesConfirmedTerminal_PublishesCheckoutCompletedEvent_AndConfirmsAllReservations()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        // Act 1 — Initial → AwaitingOrderCreation → AwaitingStockReservation
        var fanOutState = await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m),
            (product3, 3, 7.50m));

        var tracking = DeserializeTracking(fanOutState.ReservationIdsJson);

        // Act 2 — three StockReserved events fan in → AwaitingPayment
        await PublishStockReservedAsync(orderId, product1, tracking[product1].ReservationId!.Value, quantity: 1);
        await PublishStockReservedAsync(orderId, product2, tracking[product2].ReservationId!.Value, quantity: 2);
        await PublishStockReservedAsync(orderId, product3, tracking[product3].ReservationId!.Value, quantity: 3);

        var awaitingPaymentState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.AwaitingPayment, DefaultTimeout);

        // Act 3 — PaymentCompleted → AwaitingConfirmation (saga also fans out ConfirmReservationCommands here)
        var paymentTransactionId = Guid.CreateVersion7();
        await PublishPaymentCompletedAsync(correlationId, userId, paymentTransactionId, awaitingPaymentState.TotalAmount);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingConfirmation, DefaultTimeout);

        // Act 4 — OrderConfirmed → Confirmed (terminal, finalized)
        await PublishOrderConfirmedAsync(correlationId, userId, orderId);

        // Assert — saga row removed by the EF repo on Finalize() per SetCompletedWhenFinalized()
        var finalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        finalized.Should().BeTrue("the saga must reach the Confirmed terminal and be finalized by MassTransit");

        // Outbox-side assertions: every command + the saga-terminal event landed
        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var checkoutCompletedRows = outboxMessages
            .Where(om => om.Type == typeof(CheckoutCompletedEvent).FullName)
            .ToList();

        var confirmReservationRows = outboxMessages
            .Where(om => om.Type == typeof(ConfirmReservationCommand).FullName)
            .ToList();

        var confirmOrderRows = outboxMessages
            .Where(om => om.Type == typeof(ConfirmOrderCommand).FullName)
            .ToList();

        using (new AssertionScope())
        {
            checkoutCompletedRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString(),
                    "CheckoutCompletedEvent is keyed by saga CorrelationId per ADR-0008");

            confirmOrderRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(orderId.ToString(),
                    "ConfirmOrderCommand is keyed by OrderId");

            confirmReservationRows.Should().HaveCount(3, "one ConfirmReservationCommand per distinct ProductId in the basket");
            confirmReservationRows.Select(om => om.KafkaKey)
                .Should().BeEquivalentTo(
                    [product1.ToString(), product2.ToString(), product3.ToString()],
                    "fan-out keys each ConfirmReservationCommand by ProductId for Kafka partitioning");

            // Schema-level audit-fidelity (ADR-0011): the saga-terminal event type carries no
            // address-shaped public property. Catches a regression where someone extends
            // CheckoutCompletedEvent.avsc with an address field.
            AssertNoAddressFieldsOnSchema<CheckoutCompletedEvent>();

            // Wire-level audit-fidelity: the outbox payload bytes contain none of the
            // deterministic address VALUES the test seeded into the saga state via
            // BasketCheckoutInitiatedEvent.{Shipping,Billing}Address ("123 Test Street",
            // "Prague", "11000" — see CheckoutSagaTestPublishers.AddressValueWitnesses).
            // Avro binary encoding writes values as length-prefixed UTF-8 (no field names),
            // so if the orchestrator ever re-serialised the saga's address payload into the
            // saga-terminal event, those strings would appear here. The schema-level check
            // above closes the corresponding shape regression; this catches value leakage
            // through any future intermediate aliasing or projection.
            AssertNoAddressValuesInPayload(checkoutCompletedRows[0].AvroPayload);
        }
    }

    // ----- helpers (M9-local; M6's own helpers stay private to that file to keep blast radius small) -----

    private async Task<CheckoutSagaState> DriveToAwaitingStockReservationAsync(
        Guid correlationId,
        Guid userId,
        Guid orderId,
        params (Guid ProductId, int Quantity, decimal UnitPrice)[] lines)
    {
        var basketCheckoutInitiated = CheckoutSagaTestPublishers.BuildBasketCheckoutInitiatedEvent(correlationId, userId, lines);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.BasketSessions, userId, basketCheckoutInitiated);
        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingOrderCreation, DefaultTimeout);

        var orderCreated = CheckoutSagaTestPublishers.BuildOrderCreatedEvent(correlationId, userId, orderId);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.OrderingOrders, orderId, orderCreated);

        return await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingStockReservation, DefaultTimeout);
    }

    private async Task PublishStockReservedAsync(Guid orderId, Guid productId, Guid reservationId, int quantity)
    {
        var stockReserved = new StockReservedEvent
        {
            ProductId = productId,
            ReservationId = reservationId,
            OrderId = orderId,
            Quantity = quantity,
            ReservedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddMinutes(15).UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.InventoryReservations, productId, stockReserved);
    }

    private async Task PublishPaymentCompletedAsync(Guid correlationId, Guid userId, Guid paymentTransactionId, decimal amount)
    {
        var paymentCompleted = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = amount.ToAvroDecimal(4),
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, correlationId, paymentCompleted);
    }

    private async Task PublishOrderConfirmedAsync(Guid correlationId, Guid buyerId, Guid orderId)
    {
        // The saga's OrderConfirmedConsumer reads only OrderId + CorrelationId + ConfirmedAtUtc;
        // the Items / TotalAmount / Currency / BillingAddress enrichment fields (Wave 1.5/1.6 ADR-0020)
        // are nullable for FORWARD_TRANSITIVE compatibility and are unused on the saga side.
        var orderConfirmed = new OrderConfirmedEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = buyerId,
            ConfirmedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            Items = new List<OrderItemConfirmed>(),
            TotalAmount = null,
            Currency = null,
            BillingAddress = null
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.OrderingOrders, orderId, orderConfirmed);
    }

    private static Dictionary<Guid, ReservationTracking> DeserializeTracking(string json) =>
        string.IsNullOrEmpty(json) || json == "{}"
            ? new Dictionary<Guid, ReservationTracking>()
            : JsonSerializer.Deserialize<Dictionary<Guid, ReservationTracking>>(json)
              ?? new Dictionary<Guid, ReservationTracking>();

    /// <summary>
    /// Reflects on the avrogen-generated public properties of <typeparamref name="T"/> and asserts
    /// that none of the names in <see cref="AddressFieldNames"/> are present. The Avro schema is
    /// the contract — if the schema gains an address field, this assertion fails first; if not,
    /// no payload bytes can ever carry those names. Pairs with
    /// <see cref="AssertNoAddressFieldsInPayload"/> for defence-in-depth.
    /// </summary>
    private static void AssertNoAddressFieldsOnSchema<T>()
    {
        var fieldNames = typeof(T).GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var addressField in AddressFieldNames)
        {
            fieldNames.Should().NotContain(addressField,
                $"per ADR-0011 the saga-terminal Avro event {typeof(T).Name} must not declare any address-shaped field");
        }
    }

    /// <summary>
    /// UTF-8 scans the outbox <c>AvroPayload</c> bytes for any of the deterministic address
    /// VALUES the test seeded into the saga state on initiation (per
    /// <see cref="CheckoutSagaTestPublishers.AddressValueWitnesses"/>). Avro binary encoding
    /// writes string fields as length-prefixed UTF-8 (no field names), so if the orchestrator
    /// or any future projection ever re-emitted the saga-state addresses into the terminal
    /// event payload, those strings would appear in the bytes verbatim. This is the wire-level
    /// half of the ADR-0011 audit; the schema-level half is
    /// <see cref="AssertNoAddressFieldsOnSchema{T}"/>.
    /// </summary>
    private static void AssertNoAddressValuesInPayload(byte[] avroPayload)
    {
        var payloadAsString = Encoding.UTF8.GetString(avroPayload);

        foreach (var witness in CheckoutSagaTestPublishers.AddressValueWitnesses)
        {
            payloadAsString.Should().NotContain(witness,
                $"per ADR-0011 the saga-terminal event payload bytes must not contain the address value '{witness}'");
        }
    }
}
