using System.Text;
using System.Text.Json;
using Basket.Sessions;
using Checkout.Sagas;
using Inventory.Reservations;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.Orders;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;
using SagaOrchestrators.IntegrationTests.Common;

namespace SagaOrchestrators.IntegrationTests.Sagas;

/// <summary>
/// M9 end-to-end integration tests for the Checkout saga's compensation paths. Every test
/// drives the saga via real Kafka against the real EF Core saga repository in Postgres
/// (Testcontainers via <see cref="SagaIntegrationTestFixture"/>) until the saga reaches its
/// designated terminal — <see cref="CheckoutSagaOrchestrator.Compensated"/>,
/// <see cref="CheckoutSagaOrchestrator.CompensationStuck"/>, or
/// <see cref="CheckoutSagaOrchestrator.Failed"/> — then asserts the EF saga repo finalised the
/// instance (row removed) and the outbox carries the expected commands + the saga-terminal
/// Avro event with no PII payload (ADR-0011 wire-level rule).
/// </summary>
/// <remarks>
/// The compensation-timeout test triggers the schedule via direct
/// <c>IBus.Publish&lt;CompensationTimeoutExpired&gt;</c> rather than waiting on the SQL transport
/// scheduler — same tactic the M7 unit-level metric tests use (see
/// <c>CheckoutSagaMetricsEmissionTests</c>) and avoids inflating the test runtime by 60s
/// (the <c>Saga:CheckoutTimeouts:CompensationSeconds</c> testing default). The previously-armed
/// schedule will fire later but the saga is already finalised by then;
/// <c>OnMissingInstance(Discard)</c> on the timeout event handles the late delivery silently.
/// </remarks>
[Collection(nameof(SagaTestCollection))]
public class CheckoutSagaCompensationIntegrationTests : BaseSagaIntegrationTest
{
    // Bias to 15s (vs M6's 10s) for the longer M9 chains. The OrderConfirmationFails test
    // crosses 8 saga consume cycles (initiate → order → 3× stock → payment → confirm-fail →
    // refund → 3× release → cancel) and observably runs ~10s end-to-end; the extra headroom
    // keeps genuine deadlocks catchable without flake.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private SagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState> SagaStateMonitor { get; }

    public CheckoutSagaCompensationIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState>();
    }

    /// <summary>
    /// § 4 row 7 + the <c>CompensatingStockReservations</c> row pair (rows 13-14): payment
    /// fails after stock reserved → saga compensates with stock release + order cancel and
    /// reaches <see cref="CheckoutSagaOrchestrator.Compensated"/>. No refund (payment was never
    /// captured per § 6.1).
    /// </summary>
    [Fact]
    public async Task PaymentFails_AfterStockReserved_ReachesCompensatedTerminal_NoRefund_StockReleased_OrderCancelled()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        var fanOutState = await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m),
            (product3, 3, 7.50m));

        var tracking = DeserializeTracking(fanOutState.ReservationIdsJson);

        await PublishStockReservedAsync(orderId, product1, tracking[product1].ReservationId!.Value, quantity: 1);
        await PublishStockReservedAsync(orderId, product2, tracking[product2].ReservationId!.Value, quantity: 2);
        await PublishStockReservedAsync(orderId, product3, tracking[product3].ReservationId!.Value, quantity: 3);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingPayment, DefaultTimeout);

        // Act 1 — payment fails → saga transitions to CompensatingStockReservations and dispatches
        // 3× ReleaseReservationCommand + 1× CancelOrderCommand via DispatchStockReleaseAndCancelOrder.
        await PublishPaymentFailedAsync(correlationId, userId, errorCode: "INSUFFICIENT_FUNDS");

        var compensatingState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.CompensatingStockReservations, DefaultTimeout);

        compensatingState.PendingReleases.Should().Be(3, "every reserved entry must be released during compensation");

        // Act 2 — Inventory acknowledges each release + Ordering acknowledges the cancel.
        await PublishReservationReleasedAsync(orderId, product1, tracking[product1].ReservationId!.Value);
        await PublishReservationReleasedAsync(orderId, product2, tracking[product2].ReservationId!.Value);
        await PublishReservationReleasedAsync(orderId, product3, tracking[product3].ReservationId!.Value);
        await PublishOrderCancelledAsync(correlationId, userId, orderId, atStatus: OrderStatusAtTransition.StockReserved);

        // Assert — Compensated terminal reached + finalized
        var finalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        finalized.Should().BeTrue("the saga must reach the Compensated terminal once all releases land + OrderCancelled arrives");

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var releaseCommands = outboxMessages
            .Where(om => om.Type == typeof(ReleaseReservationCommand).FullName!)
            .ToList();

        var cancelCommands = outboxMessages
            .Where(om => om.Type == typeof(CancelOrderCommand).FullName!)
            .ToList();

        var refundCommands = outboxMessages
            .Where(om => om.Type == typeof(RequestRefundCommand).FullName!)
            .ToList();

        var checkoutFailedRows = outboxMessages
            .Where(om => om.Type == typeof(CheckoutFailedEvent).FullName!)
            .ToList();

        using (new AssertionScope())
        {
            releaseCommands.Should().HaveCount(3, "one ReleaseReservationCommand per reserved ProductId");
            releaseCommands.Select(om => om.KafkaKey)
                .Should().BeEquivalentTo([product1.ToString(), product2.ToString(), product3.ToString()]);

            cancelCommands.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(orderId.ToString());

            refundCommands.Should().BeEmpty(
                "no refund is needed on payment-failure-before-capture per checkout-saga.md § 6.1");

            checkoutFailedRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString());

            AssertNoAddressValuesInPayload(checkoutFailedRows[0].AvroPayload);
        }
    }

    /// <summary>
    /// § 4 row 11 + the <c>CompensatingPayment</c> + <c>CompensatingStockReservations</c> chain
    /// (rows 16 + 13-14): order confirmation fails after payment captured → saga refunds first
    /// (per § 6.1 refund-then-stock split), then releases stock + cancels order, reaches
    /// <see cref="CheckoutSagaOrchestrator.Compensated"/>.
    /// </summary>
    [Fact]
    public async Task OrderConfirmationFails_AfterPaymentCompleted_ReachesCompensatedTerminal_RefundFirst_ThenStockRelease()
    {
        // Arrange — drive saga to AwaitingConfirmation
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        var fanOutState = await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m),
            (product3, 3, 7.50m));

        var tracking = DeserializeTracking(fanOutState.ReservationIdsJson);

        await PublishStockReservedAsync(orderId, product1, tracking[product1].ReservationId!.Value, quantity: 1);
        await PublishStockReservedAsync(orderId, product2, tracking[product2].ReservationId!.Value, quantity: 2);
        await PublishStockReservedAsync(orderId, product3, tracking[product3].ReservationId!.Value, quantity: 3);

        var awaitingPaymentState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.AwaitingPayment, DefaultTimeout);

        var paymentTransactionId = Guid.CreateVersion7();
        await PublishPaymentCompletedAsync(correlationId, userId, paymentTransactionId, awaitingPaymentState.TotalAmount);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingConfirmation, DefaultTimeout);

        // Act 1 — Ordering reports the order failed during confirmation. Saga publishes
        // RequestRefundCommand and transitions to CompensatingPayment per § 4 row 11.
        await PublishOrderFailedAsync(correlationId, userId, orderId,
            errorCode: "CONFIRMATION_INVENTORY_OUT_OF_SYNC",
            atStatus: OrderStatusAtTransition.PaymentCompleted);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.CompensatingPayment, DefaultTimeout);

        // Act 2 — Payments confirms the refund; saga then dispatches stock releases + order cancel,
        // and re-arms the compensation budget for the stock-release leg per § 6.1.
        await PublishPaymentRefundedAsync(correlationId, userId, paymentTransactionId, awaitingPaymentState.TotalAmount);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.CompensatingStockReservations, DefaultTimeout);

        // Act 3 — Inventory acknowledges releases + Ordering acknowledges cancel
        await PublishReservationReleasedAsync(orderId, product1, tracking[product1].ReservationId!.Value);
        await PublishReservationReleasedAsync(orderId, product2, tracking[product2].ReservationId!.Value);
        await PublishReservationReleasedAsync(orderId, product3, tracking[product3].ReservationId!.Value);
        await PublishOrderCancelledAsync(correlationId, userId, orderId, atStatus: OrderStatusAtTransition.PaymentCompleted);

        // Assert — Compensated terminal reached + finalized
        var finalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        finalized.Should().BeTrue("Compensated is reached once refund + all releases + cancel land");

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .OrderBy(om => om.Id)
            .ToListAsync();

        var refundCommands = outboxMessages
            .Where(om => om.Type == typeof(RequestRefundCommand).FullName!)
            .ToList();

        var releaseCommands = outboxMessages
            .Where(om => om.Type == typeof(ReleaseReservationCommand).FullName!)
            .ToList();

        var cancelCommands = outboxMessages
            .Where(om => om.Type == typeof(CancelOrderCommand).FullName!)
            .ToList();

        var checkoutFailedRows = outboxMessages
            .Where(om => om.Type == typeof(CheckoutFailedEvent).FullName!)
            .ToList();

        using (new AssertionScope())
        {
            refundCommands.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString());

            releaseCommands.Should().HaveCount(3);
            cancelCommands.Should().ContainSingle();

            // refund-first ordering (§ 6.1): the refund command is enqueued ahead of the stock releases
            refundCommands[0].Id.Should().BeLessThan(releaseCommands.Min(rc => rc.Id),
                "per § 6.1 refund precedes stock release in the compensating-payment chain");

            checkoutFailedRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString());

            AssertNoAddressValuesInPayload(checkoutFailedRows[0].AvroPayload);
        }
    }

    /// <summary>
    /// § 4 row 15 (CompensationTimeout in <see cref="CheckoutSagaOrchestrator.CompensatingStockReservations"/>):
    /// during compensation the saga's stock-release leg never completes; CompensationTimeout fires
    /// → saga reaches <see cref="CheckoutSagaOrchestrator.CompensationStuck"/> abnormal-terminal,
    /// publishes <see cref="CheckoutStuckEvent"/> with the runbook-investigation fields per
    /// <c>saga-stuck-runbook.md § 3</c>.
    /// </summary>
    [Fact]
    public async Task CompensationTimeout_DuringStockRelease_ReachesCompensationStuckTerminal_AndPublishesCheckoutStuckEvent()
    {
        // Arrange — drive saga into CompensatingStockReservations via stock-reservation-failure
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        var fanOutState = await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m));

        var tracking = DeserializeTracking(fanOutState.ReservationIdsJson);

        // product1 succeeds, product2 fails → saga transitions to CompensatingStockReservations
        // with PendingReleases=1 (only product1 is Reserved and needs release).
        await PublishStockReservedAsync(orderId, product1, tracking[product1].ReservationId!.Value, quantity: 1);
        await WaitForReservedStatusAsync(correlationId, product1);
        await PublishStockReservationFailedAsync(orderId, product2, requested: 2, available: 0);

        await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.CompensatingStockReservations, DefaultTimeout);

        // Act — withhold the ReservationReleasedEvent and OrderCancelledEvent; instead publish
        // the CompensationTimeoutExpired schedule event directly. Same tactic as the M7 unit-level
        // metric tests; avoids waiting Saga:CheckoutTimeouts:CompensationSeconds (=60s in Testing).
        await Bus.Publish(new CompensationTimeoutExpired { CorrelationId = correlationId });

        // Assert — CompensationStuck is abnormal-terminal; saga finalised + CheckoutStuckEvent emitted.
        var finalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        finalized.Should().BeTrue("CompensationTimeout fires the CompensationStuck terminal transition + Finalize()");

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var checkoutStuckRows = outboxMessages
            .Where(om => om.Type == typeof(CheckoutStuckEvent).FullName!)
            .ToList();

        using (new AssertionScope())
        {
            checkoutStuckRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString(),
                    "CheckoutStuckEvent is keyed by saga CorrelationId per ADR-0008");

            // saga-stuck-runbook.md § 3 Investigation: every stuck-saga payload must surface
            // correlation_id, last_state, stuck_since_utc, failure_reason for the on-call to
            // open Jaeger / Seq with. AvroPayload is binary but UTF-8 strings appear verbatim.
            var payload = Encoding.UTF8.GetString(checkoutStuckRows[0].AvroPayload);
            payload.Should().Contain(nameof(CheckoutSagaOrchestrator.CompensatingStockReservations),
                "CheckoutStuckEvent.LastState carries the state name the saga was stuck in");
            payload.Should().Contain("COMPENSATION_TIMEOUT",
                "CheckoutStuckEvent.ErrorCode is COMPENSATION_TIMEOUT for timeout-driven stuck");

            AssertNoAddressValuesInPayload(checkoutStuckRows[0].AvroPayload);
        }
    }

    /// <summary>
    /// § 4 row 4: order creation fails before any stock has been touched → saga reaches
    /// <see cref="CheckoutSagaOrchestrator.Failed"/> directly, with no compensation needed.
    /// </summary>
    [Fact]
    public async Task OrderFails_BeforeStockTouched_ReachesFailedTerminal_NoCompensationNeeded()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        var basketCheckoutInitiated = CheckoutSagaTestPublishers.BuildBasketCheckoutInitiatedEvent(
            correlationId, userId, [(product1, 1, 10m)]);

        await KafkaTestProducer.ProduceAsync(TopicsOptions.BasketSessions, userId, basketCheckoutInitiated);
        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingOrderCreation, DefaultTimeout);

        // Act — Ordering rejects the create-order request
        await PublishOrderFailedAsync(correlationId, userId, orderId,
            errorCode: "ORDER_VALIDATION_FAILED",
            atStatus: OrderStatusAtTransition.Created);

        // Assert — Failed terminal, no compensation outbox commands (nothing to compensate).
        var finalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        finalized.Should().BeTrue();

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var releaseCommands = outboxMessages
            .Where(om => om.Type == typeof(ReleaseReservationCommand).FullName!)
            .ToList();

        var cancelCommands = outboxMessages
            .Where(om => om.Type == typeof(CancelOrderCommand).FullName!)
            .ToList();

        var checkoutFailedRows = outboxMessages
            .Where(om => om.Type == typeof(CheckoutFailedEvent).FullName!)
            .ToList();

        using (new AssertionScope())
        {
            releaseCommands.Should().BeEmpty("nothing was reserved → nothing to release");
            cancelCommands.Should().BeEmpty("OrderId is unknown on failure-before-AwaitingStockReservation → no cancel command");
            checkoutFailedRows.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(correlationId.ToString());

            AssertNoAddressValuesInPayload(checkoutFailedRows[0].AvroPayload);
        }
    }

    // ----- helpers -----

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

    private async Task PublishStockReservationFailedAsync(Guid orderId, Guid productId, int requested, int available)
    {
        var stockFailed = new StockReservationFailedEvent
        {
            ProductId = productId,
            OrderId = orderId,
            RequestedQuantity = requested,
            AvailableQuantity = available,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.InventoryReservations, productId, stockFailed);
    }

    private async Task PublishReservationReleasedAsync(Guid orderId, Guid productId, Guid reservationId)
    {
        var released = new ReservationReleasedEvent
        {
            ProductId = productId,
            ReservationId = reservationId,
            OrderId = orderId,
            ReleaseReason = ReleaseReason.Compensation,
            ReleasedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.InventoryReservations, productId, released);
    }

    private async Task PublishPaymentFailedAsync(Guid correlationId, Guid userId, string errorCode)
    {
        var paymentFailed = new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = errorCode,
            ErrorMessage = $"Test-injected payment failure: {errorCode}",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPayments, correlationId, paymentFailed);
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

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPayments, correlationId, paymentCompleted);
    }

    private async Task PublishPaymentRefundedAsync(Guid correlationId, Guid userId, Guid paymentTransactionId, decimal amount)
    {
        var refunded = new PaymentRefundedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = amount.ToAvroDecimal(4),
            Currency = "USD",
            RefundedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPayments, correlationId, refunded);
    }

    private async Task PublishOrderFailedAsync(
        Guid correlationId, Guid userId, Guid orderId,
        string errorCode, OrderStatusAtTransition atStatus)
    {
        var orderFailed = new OrderFailedEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = userId,
            ErrorCode = errorCode,
            ErrorMessage = $"Test-injected order failure: {errorCode}",
            AtStatus = atStatus,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.OrderingOrders, orderId, orderFailed);
    }

    private async Task PublishOrderCancelledAsync(
        Guid correlationId, Guid userId, Guid orderId, OrderStatusAtTransition atStatus)
    {
        // OrderCancelledEvent.Items / TotalAmount / Currency / BillingAddress are nullable for
        // FORWARD_TRANSITIVE per ADR-0020; the saga's OrderCancelledConsumer reads only OrderId
        // + CorrelationId + CancelledAtUtc, so the empty/null enrichment fields are ignored here.
        var orderCancelled = new OrderCancelledEvent
        {
            OrderId = orderId,
            CorrelationId = correlationId,
            BuyerId = userId,
            Reason = "Checkout saga compensation (test)",
            AtStatus = atStatus,
            CancelledAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            Items = new List<OrderItemCancelled>(),
            TotalAmount = null,
            Currency = null,
            BillingAddress = null
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.OrderingOrders, orderId, orderCancelled);
    }

    /// <summary>
    /// Polls the persisted saga state until <paramref name="productId"/>'s tracking entry is
    /// <see cref="ReservationStatus.Reserved"/>. Used to sequence operations where the saga must
    /// observe a StockReservedEvent before a downstream event arrives — same race-pre-mortem
    /// rationale M6's <c>WaitForStateConditionAsync</c> records.
    /// </summary>
    private async Task WaitForReservedStatusAsync(Guid correlationId, Guid productId)
    {
        var deadline = DateTime.UtcNow + DefaultTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var state = await SagaDbContext.CheckoutSagaStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

            if (state is not null)
            {
                var current = DeserializeTracking(state.ReservationIdsJson);
                if (current.TryGetValue(productId, out var entry) && entry.Status == ReservationStatus.Reserved)
                {
                    return;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Saga {correlationId} did not register product {productId} as Reserved within {DefaultTimeout.TotalSeconds}s.");
    }

    private static Dictionary<Guid, ReservationTracking> DeserializeTracking(string json) =>
        string.IsNullOrEmpty(json) || json == "{}"
            ? new Dictionary<Guid, ReservationTracking>()
            : JsonSerializer.Deserialize<Dictionary<Guid, ReservationTracking>>(json)
              ?? new Dictionary<Guid, ReservationTracking>();

    /// <summary>
    /// Wire-level audit-fidelity (ADR-0011): outbox payload bytes must not contain any of the
    /// deterministic address VALUES the test seeded into the saga state on initiation. See
    /// <see cref="CheckoutSagaTestPublishers.AddressValueWitnesses"/> for the rationale (Avro
    /// binary encoding writes string values as length-prefixed UTF-8; the prior M9 draft
    /// scanned for field NAMES which Avro never emits — see Opus review H1).
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
