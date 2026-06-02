using System.Text.Json;
using Basket.Sessions;
using Inventory.Reservations;
using Microsoft.EntityFrameworkCore;
using Ordering.Orders;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.Test.Framework.Assertions;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;
using SagaOrchestrators.IntegrationTests.Common;

namespace SagaOrchestrators.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the Checkout saga's multi-item fan-out flow (§ 5 fan-out
/// algorithm + § 4 transition-table rows 2 and 6). Drives the saga via real Kafka
/// (<c>basket.sessions</c>, <c>ordering.orders</c>, <c>inventory.reservations</c>) against
/// the real EF Core saga repository in Postgres (Testcontainers via
/// <see cref="SagaIntegrationTestFixture"/>); asserts on the persisted
/// <see cref="CheckoutSagaState"/> rows and on outbox commands written by the saga.
/// </summary>
/// <remarks>
/// BC services are NOT running for these tests — the test publishes the response Avro
/// events directly (mimicking what Ordering / Inventory would send back) so the scope
/// stays focused on the saga's fan-out machinery. This file stops at the entry to
/// <see cref="CheckoutSagaOrchestrator.CompensatingStockReservations"/>; the
/// full end-to-end BC-driven run plus the ADR-0011 PII null-out check live in
/// <see cref="CheckoutSagaEndToEndIntegrationTests"/> and
/// <see cref="CheckoutSagaCompensationIntegrationTests"/>.
/// </remarks>
[Collection(nameof(SagaTestCollection))]
public class CheckoutSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private SagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState> SagaStateMonitor { get; }

    public CheckoutSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<CheckoutSagaOrchestrator, CheckoutSagaState>();
    }

    [Fact]
    public async Task WhenBasketCheckoutInitiated_WithThreeDistinctProducts_FansOutThreeReserveStockCommands_AndReachesAwaitingStockReservation()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        // Act
        await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m),
            (product3, 3, 7.50m));

        // Assert
        var persistedState = await SagaDbContext.CheckoutSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var reserveCommands = outboxMessages
            .Where(om => om.Type == typeof(ReserveStockCommand).FullName)
            .ToList();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState!.OrderId.Should().Be(orderId);
            persistedState.ExpectedReservations.Should().Be(3);
            persistedState.PendingReservations.Should().Be(3);
            persistedState.StockReservationStartedAtUtc.Should().NotBeNull();

            outboxMessages.Should().ContainMessageOfType<CreateOrderCommand>(correlationId.ToString());

            reserveCommands.Should().HaveCount(3);
            reserveCommands.Select(om => om.KafkaKey)
                .Should().BeEquivalentTo(
                    [product1.ToString(), product2.ToString(), product3.ToString()],
                    "fan-out keys each ReserveStockCommand by ProductId for Kafka partitioning");

            var tracking = DeserializeTracking(persistedState.ReservationIdsJson);
            tracking.Should().HaveCount(3);
            tracking.Should().ContainKeys(product1, product2, product3);
            tracking.Values.Should().AllSatisfy(entry =>
            {
                entry.Status.Should().Be(ReservationStatus.Pending);
                entry.ReservationId.Should().NotBeNull().And.NotBe(Guid.Empty);
            });
            tracking.Values.Select(t => t.ReservationId!.Value)
                .Should().OnlyHaveUniqueItems("each saga-minted ReservationId must be unique (UUID v7)");
        }
    }

    [Fact]
    public async Task WhenBasketHasDuplicateProductLines_FanOutCoalescesQuantities_IntoOneCommandPerDistinctProductId()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product = Guid.CreateVersion7();

        // Two lines, same ProductId, total quantity 2 + 3 = 5.
        // Act
        await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product, 2, 10m),
            (product, 3, 10m));

        // Assert
        var persistedState = await SagaDbContext.CheckoutSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var reserveCommands = outboxMessages
            .Where(om => om.Type == typeof(ReserveStockCommand).FullName)
            .ToList();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState!.ExpectedReservations.Should().Be(1, "duplicate ProductIds collapse to one fan-out entry");
            persistedState.PendingReservations.Should().Be(1);

            reserveCommands.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(product.ToString());

            var tracking = DeserializeTracking(persistedState.ReservationIdsJson);
            tracking.Should().ContainSingle()
                .Which.Key.Should().Be(product);
        }
    }

    [Fact]
    public async Task WhenAllStockReservedEventsArrive_TransitionsToAwaitingPayment_AndPublishesPaymentRequested()
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

        // Act — emit one Inventory StockReservedEvent per product, echoing the saga-minted ReservationId.
        await PublishStockReservedAsync(orderId, product1, tracking[product1].ReservationId!.Value, quantity: 1);
        await PublishStockReservedAsync(orderId, product2, tracking[product2].ReservationId!.Value, quantity: 2);
        await PublishStockReservedAsync(orderId, product3, tracking[product3].ReservationId!.Value, quantity: 3);

        // Assert
        var awaitingPaymentState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.AwaitingPaymentAuthorization, DefaultTimeout);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            awaitingPaymentState.PendingReservations.Should().Be(0);
            awaitingPaymentState.StockReservationCompletedAtUtc.Should().NotBeNull();
            awaitingPaymentState.PaymentRequestedAtUtc.Should().NotBeNull();

            var trackingAfterFanIn = DeserializeTracking(awaitingPaymentState.ReservationIdsJson);
            trackingAfterFanIn.Values.Should().AllSatisfy(entry =>
                entry.Status.Should().Be(ReservationStatus.Reserved));

            outboxMessages.Should().ContainSingleMessageOfType<RequestPaymentCommand>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenStockReservationFailsForOne_AndOthersAreReserved_TransitionsToCompensating_AndReleasesOnlyTheReservedOnes()
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
        var product1ReservationId = tracking[product1].ReservationId!.Value;

        // Act 1 — only product1 succeeds (1 of 3); wait for the saga to record it as Reserved
        // before sending the failure, otherwise the failure may transition the saga to
        // CompensatingStockReservations before the StockReservedEvent for product1 arrives,
        // producing a flaky outcome (zero releases instead of one).
        await PublishStockReservedAsync(orderId, product1, product1ReservationId, quantity: 1);

        await WaitForStateConditionAsync(
            correlationId,
            state =>
            {
                if (state.PendingReservations != 2)
                {
                    return false;
                }

                var current = DeserializeTracking(state.ReservationIdsJson);
                return current.TryGetValue(product1, out var entry)
                       && entry.Status == ReservationStatus.Reserved;
            },
            DefaultTimeout);

        // Act 2 — product2 fails. Per § 4 row 6 the saga transitions to compensating,
        // only product1 is in Reserved, so only its release should be dispatched.
        await PublishStockReservationFailedAsync(orderId, product2, requested: 2, available: 0);

        // Assert
        var compensatingState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.CompensatingStockReservations, DefaultTimeout);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var releaseCommands = outboxMessages
            .Where(om => om.Type == typeof(ReleaseReservationCommand).FullName)
            .ToList();

        var cancelCommands = outboxMessages
            .Where(om => om.Type == typeof(CancelOrderCommand).FullName)
            .ToList();

        using (new AssertionScope())
        {
            compensatingState.CompensationTriggered.Should().BeTrue();
            compensatingState.PendingReleases.Should().Be(1, "only product1 is Reserved; product2 failed and product3 is still Pending");
            compensatingState.ErrorCode.Should().Be(CheckoutSagaErrorCodes.StockUnavailable);
            compensatingState.FailedAtState.Should().Be(nameof(CheckoutSagaOrchestrator.AwaitingStockReservation));

            releaseCommands.Should().ContainSingle("only product1 holds an active reservation needing release")
                .Which.KafkaKey.Should().Be(product1.ToString());

            cancelCommands.Should().ContainSingle()
                .Which.KafkaKey.Should().Be(orderId.ToString());

            var trackingAfterFailure = DeserializeTracking(compensatingState.ReservationIdsJson);
            trackingAfterFailure[product1].Status.Should().Be(ReservationStatus.Reserved);
            trackingAfterFailure[product2].Status.Should().Be(ReservationStatus.Failed);
            trackingAfterFailure[product3].Status.Should().Be(ReservationStatus.Pending);
        }
    }

    [Fact]
    public async Task WhenStockReservedEventArrivesTwiceForSameReservationId_SecondCopyIsNoOp_AndCounterStaysCorrect()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        var fanOutState = await DriveToAwaitingStockReservationAsync(correlationId, userId, orderId,
            (product1, 1, 10m),
            (product2, 2, 25m));

        var tracking = DeserializeTracking(fanOutState.ReservationIdsJson);
        var product1ReservationId = tracking[product1].ReservationId!.Value;
        var product2ReservationId = tracking[product2].ReservationId!.Value;

        // Act — duplicate StockReservedEvent for product1, then product2's reserved event.
        // The idempotency guard in UpdateReservationOnReserved must skip the duplicate so
        // PendingReservations decrements from 2 → 1 → 0 (not 2 → 1 → -1) and the saga still
        // transitions to AwaitingPaymentAuthorization exactly once.
        await PublishStockReservedAsync(orderId, product1, product1ReservationId, quantity: 1);
        await PublishStockReservedAsync(orderId, product1, product1ReservationId, quantity: 1);
        await PublishStockReservedAsync(orderId, product2, product2ReservationId, quantity: 2);

        // Assert
        var awaitingPaymentState = await SagaStateMonitor.WaitForStateAsync(
            correlationId, x => x.AwaitingPaymentAuthorization, DefaultTimeout);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            awaitingPaymentState.PendingReservations.Should().Be(0,
                "the duplicate StockReservedEvent is skipped by the Pending-status guard");

            outboxMessages
                .Where(om => om.Type == typeof(RequestPaymentCommand).FullName
                             && om.KafkaKey == correlationId.ToString())
                .Should().ContainSingle("the AwaitingPaymentAuthorization transition runs exactly once even with duplicate fan-in");
        }
    }

    // ----- helpers -----

    /// <summary>
    /// Drives the saga from <c>Initial</c> through to <c>AwaitingStockReservation</c> by
    /// publishing a <see cref="BasketCheckoutInitiatedEvent"/> on <c>basket.sessions</c> and a
    /// synthetic <see cref="OrderCreatedEvent"/> on <c>ordering.orders</c>. Returns the
    /// persisted <see cref="CheckoutSagaState"/> snapshot taken once <c>AwaitingStockReservation</c>
    /// is reached so callers can read the saga-minted <c>ReservationId</c>s for downstream
    /// fan-in steps.
    /// </summary>
    private async Task<CheckoutSagaState> DriveToAwaitingStockReservationAsync(
        Guid correlationId,
        Guid userId,
        Guid orderId,
        params (Guid ProductId, int Quantity, decimal UnitPrice)[] lines)
    {
        var basketCheckoutInitiated = CreateBasketCheckoutInitiatedEvent(correlationId, userId, lines);

        await KafkaTestProducer.ProduceAsync(TopicsOptions.BasketSessions, userId, basketCheckoutInitiated);
        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingOrderCreation, DefaultTimeout);

        var orderCreated = CreateOrderCreatedEvent(correlationId, userId, orderId);
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

    /// <summary>
    /// Polls the persisted saga state until <paramref name="predicate"/> holds. Used when the
    /// target invariant (e.g., <c>PendingReservations == 2 AND product1.Status == Reserved</c>)
    /// is finer-grained than a state-transition the <see cref="SagaStateMonitor"/> can wait on.
    /// </summary>
    private async Task<CheckoutSagaState> WaitForStateConditionAsync(
        Guid correlationId,
        Func<CheckoutSagaState, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var state = await SagaDbContext.CheckoutSagaStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            $"Saga {nameof(CheckoutSagaState)} with CorrelationId {correlationId} " +
            $"did not satisfy the expected condition within {timeout.TotalSeconds}s.");
    }

    private static BasketCheckoutInitiatedEvent CreateBasketCheckoutInitiatedEvent(
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

        var address = CreateAddress();

        return new BasketCheckoutInitiatedEvent
        {
            BasketCorrelationId = correlationId,
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
    /// Builds a synthetic <see cref="OrderCreatedEvent"/> for the saga's <see cref="OrderCreatedConsumer"/>
    /// to consume. The consumer only reads <c>OrderId</c>, <c>CorrelationId</c>, and <c>CreatedAtUtc</c>;
    /// remaining fields are populated solely to satisfy the Avro schema.
    /// </summary>
    private static OrderCreatedEvent CreateOrderCreatedEvent(
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

    private static CheckoutAddress CreateAddress() =>
        new()
        {
            Street1 = "123 Test Street",
            Street2 = null,
            City = "Prague",
            State = null,
            PostalCode = "11000",
            CountryCode = "CZ"
        };

    private static Dictionary<Guid, ReservationTracking> DeserializeTracking(string json) =>
        string.IsNullOrEmpty(json) || json == "{}"
            ? new Dictionary<Guid, ReservationTracking>()
            : JsonSerializer.Deserialize<Dictionary<Guid, ReservationTracking>>(json)
              ?? new Dictionary<Guid, ReservationTracking>();
}
