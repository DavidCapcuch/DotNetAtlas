using System.Text.Json;
using Checkout.Sagas;
using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OpenFeature;
using Ordering.Orders;
using Payments.Transactions;
using Platform.Test.Framework.Kafka;
using SagaOrchestrators.Checkout.CheckoutSaga;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.UnitTests.Checkout;

namespace SagaOrchestrators.UnitTests.Sagas;

/// <summary>
/// Unit tests for the <see cref="CheckoutSagaOrchestrator"/> state machine. Covers every
/// event-driven and timeout-driven cell of the § 4 transition table.
/// </summary>
/// <remarks>
/// Participates in <see cref="CheckoutMeterSerialCollection"/>: this class drives the same
/// compensation transitions that <c>CheckoutSagaMetricsEmissionTests</c> asserts
/// on via <see cref="System.Diagnostics.Metrics.MeterListener"/>; the shared collection
/// serialises the two so the listener doesn't observe cross-class measurements on the
/// process-global <c>SagaOrchestrators</c> meter.
/// </remarks>
[Collection(nameof(CheckoutMeterSerialCollection))]
public class CheckoutSagaOrchestratorTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _testHarness = null!;
    private ISagaStateMachineTestHarness<CheckoutSagaOrchestrator, CheckoutSagaState> _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<CheckoutSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            // M8: every Checkout-saga flag-key resolves to OFF in this fixture so the M2 — M7
            // tests continue to assert the default stock-then-payment topology unchanged.
            .AddSingleton<IFeatureClient>(CheckoutFeatureClientStub.WithPaymentThenStock(false))
            .AddSagaOutboxTestServices(testDbName, _fakeOutboxWriter)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<CheckoutSagaOrchestrator, CheckoutSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _testHarness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _testHarness.GetSagaStateMachineHarness<CheckoutSagaOrchestrator, CheckoutSagaState>();
        await _testHarness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _testHarness.Stop();
        await _provider.DisposeAsync();
    }

    // ===== M2 structural smoke tests (kept) =====

    [Fact]
    public void Constructor_ShouldDeclareAllExplicitStates()
    {
        var stateMachine = _sagaHarness.StateMachine;

        using (new AssertionScope())
        {
            stateMachine.AwaitingOrderCreation.Should().NotBeNull();
            stateMachine.AwaitingStockReservation.Should().NotBeNull();
            stateMachine.AwaitingPayment.Should().NotBeNull();
            stateMachine.AwaitingConfirmation.Should().NotBeNull();
            stateMachine.Confirmed.Should().NotBeNull();
            stateMachine.CompensatingStockReservations.Should().NotBeNull();
            stateMachine.CompensatingPayment.Should().NotBeNull();
            stateMachine.Compensated.Should().NotBeNull();
            stateMachine.Failed.Should().NotBeNull();
            stateMachine.CompensationStuck.Should().NotBeNull();
        }
    }

    [Fact]
    public void Constructor_ShouldRegisterAllTwelveSagaEvents()
    {
        var stateMachine = _sagaHarness.StateMachine;

        using (new AssertionScope())
        {
            stateMachine.BasketCheckoutInitiatedEvent.Should().NotBeNull();
            stateMachine.OrderCreatedEvent.Should().NotBeNull();
            stateMachine.OrderFailedEvent.Should().NotBeNull();
            stateMachine.OrderCancelledEvent.Should().NotBeNull();
            stateMachine.OrderConfirmedEvent.Should().NotBeNull();
            stateMachine.StockReservedEvent.Should().NotBeNull();
            stateMachine.StockReservationFailedEvent.Should().NotBeNull();
            stateMachine.ReservationReleasedEvent.Should().NotBeNull();
            stateMachine.ReservationConfirmedEvent.Should().NotBeNull();
            stateMachine.PaymentCompletedEvent.Should().NotBeNull();
            stateMachine.PaymentFailedEvent.Should().NotBeNull();
            stateMachine.PaymentRefundedEvent.Should().NotBeNull();
        }
    }

    // ===== § 4 row 1: Initial -> AwaitingOrderCreation =====

    [Fact]
    public async Task Initial_OnBasketCheckoutInitiated_TransitionsToAwaitingOrderCreation_AndPublishesCreateOrderCommand()
    {
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        var sagaEvent = BuildBasketCheckoutInitiated(correlationId, userId, paymentMethodId,
            BuildItem(product1, qty: 2), BuildItem(product2, qty: 1));

        await _testHarness.Bus.Publish(sagaEvent);

        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var state = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingOrderCreation);
        var outboxMessages = _fakeOutboxWriter.GetMessages<CreateOrderCommand>().ToList();

        using (new AssertionScope())
        {
            state.Should().NotBeNull();
            state.UserId.Should().Be(userId);
            state.TotalAmount.Should().BeGreaterThan(0m);
            state.Currency.Should().Be("USD");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.BuyerId.Should().Be(userId);
            outboxMessages[0].IntegrationEvent.PaymentMethodId.Should().Be(paymentMethodId);
            outboxMessages[0].IntegrationEvent.Items.Should().HaveCount(2);
        }
    }

    // ===== § 4 row 2: AwaitingOrderCreation -> AwaitingStockReservation (fan-out) =====

    [Fact]
    public async Task AwaitingOrderCreation_OnOrderCreated_TransitionsToAwaitingStockReservation_AndFansOutOneReserveStockPerDistinctProduct()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        await PublishInitiated(correlationId,
            BuildItem(product1, qty: 2),
            BuildItem(product1, qty: 3), // duplicate ProductId across lines - sums to 5
            BuildItem(product2, qty: 1),
            BuildItem(product3, qty: 4));

        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<OrderCreatedSagaEvent>()).Should().BeTrue();

        var state = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingStockReservation);
        var reserveCommands = _fakeOutboxWriter.GetMessages<ReserveStockCommand>().ToList();

        using (new AssertionScope())
        {
            state.Should().NotBeNull();
            state.OrderId.Should().Be(orderId);
            state.ExpectedReservations.Should().Be(3, "3 distinct ProductIds");
            state.PendingReservations.Should().Be(3);
            reserveCommands.Should().HaveCount(3);
            reserveCommands.Select(m => m.IntegrationEvent.ProductId).Should()
                .BeEquivalentTo(new[] { product1, product2, product3 });
            reserveCommands.Single(m => m.IntegrationEvent.ProductId == product1)
                .IntegrationEvent.Quantity.Should().Be(5, "summed across duplicate ProductId lines");
        }
    }

    // ===== § 4 row 3: AwaitingOrderCreation -> Failed (no compensation) =====

    [Fact]
    public async Task AwaitingOrderCreation_OnOrderFailed_TransitionsToFailed_AndPublishesCheckoutFailedEventWithoutCompensation()
    {
        var correlationId = Guid.CreateVersion7();
        await PublishInitiated(correlationId, BuildItem(Guid.CreateVersion7()));
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new OrderFailedSagaEvent
        {
            CorrelationId = correlationId,
            ErrorCode = "ORDER_VALIDATION_FAILED",
            ErrorMessage = "Buyer not found",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<OrderFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var failedEvents = _fakeOutboxWriter.GetMessages<CheckoutFailedEvent>().ToList();

        using (new AssertionScope())
        {
            sagaNotExists.Should().BeTrue("Failed is terminal and finalized");
            failedEvents.Should().ContainSingle();
            failedEvents[0].IntegrationEvent.ErrorCode.Should().Be("ORDER_VALIDATION_FAILED");
            failedEvents[0].IntegrationEvent.FailedAtState.Should().Be(nameof(_sagaHarness.StateMachine.AwaitingOrderCreation));
            failedEvents[0].IntegrationEvent.CompensationTriggered.Should().BeFalse();
            // No compensation -> no CancelOrderCommand or ReleaseReservationCommand should be emitted.
            _fakeOutboxWriter.HasMessage<CancelOrderCommand>().Should().BeFalse();
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeFalse();
        }
    }

    // ===== § 4 row 5: AwaitingStockReservation - intermediate / fan-in =====

    [Fact]
    public async Task AwaitingStockReservation_WhenOneOfNStockReservedArrives_StaysInState_AndDecrementsPendingReservations()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId,
            BuildItem(product1), BuildItem(product2));

        var stateBefore = _sagaHarness.Sagas.Contains(correlationId)!;
        var firstReservationId = GetReservationId(stateBefore, product1);

        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = firstReservationId,
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });

        (await _sagaHarness.Consumed.Any<StockReservedSagaEvent>()).Should().BeTrue();

        var stillWaiting = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingStockReservation);

        using (new AssertionScope())
        {
            stillWaiting.Should().NotBeNull("only 1 of 2 reservations confirmed");
            stillWaiting.PendingReservations.Should().Be(1);
        }
    }

    [Fact]
    public async Task AwaitingStockReservation_WhenAllStockReservedArrive_TransitionsToAwaitingPayment_AndPublishesPaymentRequested()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId,
            BuildItem(product1), BuildItem(product2));
        _fakeOutboxWriter.Clear();

        var state = _sagaHarness.Sagas.Contains(correlationId)!;
        var rid1 = GetReservationId(state, product1);
        var rid2 = GetReservationId(state, product2);

        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = rid1,
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });
        await WaitForConsumed<StockReservedSagaEvent>(1);

        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product2,
            ReservationId = rid2,
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });
        await WaitForConsumed<StockReservedSagaEvent>(2);

        var awaitingPayment = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingPayment);
        var paymentRequested = _fakeOutboxWriter.GetMessages<PaymentRequestedEvent>().ToList();

        using (new AssertionScope())
        {
            awaitingPayment.Should().NotBeNull();
            awaitingPayment.PendingReservations.Should().Be(0);
            awaitingPayment.StockReservationCompletedAtUtc.Should().NotBeNull();
            paymentRequested.Should().ContainSingle();
            paymentRequested[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            paymentRequested[0].IntegrationEvent.OrderId.Should().Be(orderId);
        }
    }

    // ===== § 4 row 6: AwaitingStockReservation -> CompensatingStockReservations =====

    [Fact]
    public async Task AwaitingStockReservation_OnStockReservationFailed_TransitionsToCompensating_AndReleasesAlreadyReservedAndCancelsOrder()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId,
            BuildItem(product1), BuildItem(product2), BuildItem(product3));

        // product1 reserved, product3 reserved, product2 fails -> compensation releases p1 + p3
        var state = _sagaHarness.Sagas.Contains(correlationId)!;
        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = GetReservationId(state, product1),
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });
        await WaitForConsumed<StockReservedSagaEvent>(1);

        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product3,
            ReservationId = GetReservationId(state, product3),
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });
        await WaitForConsumed<StockReservedSagaEvent>(2);

        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new StockReservationFailedSagaEvent
        {
            OrderId = orderId,
            ProductId = product2,
            RequestedQuantity = 1,
            AvailableQuantity = 0,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<StockReservationFailedSagaEvent>()).Should().BeTrue();

        var compensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);
        var releases = _fakeOutboxWriter.GetMessages<ReleaseReservationCommand>().ToList();
        var cancels = _fakeOutboxWriter.GetMessages<CancelOrderCommand>().ToList();

        using (new AssertionScope())
        {
            compensating.Should().NotBeNull();
            compensating.CompensationTriggered.Should().BeTrue();
            compensating.ErrorCode.Should().Be("STOCK_UNAVAILABLE");
            compensating.PendingReleases.Should().Be(2, "p1 + p3 had been Reserved");
            releases.Should().HaveCount(2);
            releases.Select(r => r.IntegrationEvent.ProductId).Should()
                .BeEquivalentTo(new[] { product1, product3 });
            releases.Should().NotContain(r => r.IntegrationEvent.ProductId == product2,
                "the failed product had no Reserved entry to release");
            cancels.Should().ContainSingle();
            cancels[0].IntegrationEvent.OrderId.Should().Be(orderId);
        }
    }

    // ===== § 4 row 7: AwaitingPayment -> AwaitingConfirmation =====

    [Fact]
    public async Task AwaitingPayment_OnPaymentCompleted_TransitionsToAwaitingConfirmation_AndPublishesConfirmOrderAndPerReservationConfirms()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await ReachAwaitingPayment(correlationId, orderId, product1, product2);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new PaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            PaymentTransactionId = Guid.CreateVersion7(),
            Amount = 19.98m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<PaymentCompletedSagaEvent>()).Should().BeTrue();

        var awaitingConfirmation = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingConfirmation);
        var confirmOrders = _fakeOutboxWriter.GetMessages<ConfirmOrderCommand>().ToList();
        var confirmReservations = _fakeOutboxWriter.GetMessages<ConfirmReservationCommand>().ToList();

        using (new AssertionScope())
        {
            awaitingConfirmation.Should().NotBeNull();
            awaitingConfirmation.PaymentTransactionId.Should().NotBeNull();
            confirmOrders.Should().ContainSingle();
            confirmOrders[0].IntegrationEvent.OrderId.Should().Be(orderId);
            confirmReservations.Should().HaveCount(2);
            confirmReservations.Select(c => c.IntegrationEvent.ProductId).Should()
                .BeEquivalentTo(new[] { product1, product2 });
        }
    }

    // ===== § 4 row 8: AwaitingPayment -> CompensatingStockReservations (no refund) =====

    [Fact]
    public async Task AwaitingPayment_OnPaymentFailed_TransitionsToCompensatingStockReservations_AndDoesNotPublishRequestRefund()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingPayment(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new PaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            ErrorCode = "PAYMENT_FAILED",
            ErrorMessage = "Card declined",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<PaymentFailedSagaEvent>()).Should().BeTrue();

        var compensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);

        using (new AssertionScope())
        {
            compensating.Should().NotBeNull();
            compensating.ErrorCode.Should().Be("PAYMENT_FAILED");
            _fakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeFalse(
                "payment never captured - no refund needed");
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeTrue();
            _fakeOutboxWriter.HasMessage<CancelOrderCommand>().Should().BeTrue();
        }
    }

    // ===== § 4 row 10: AwaitingConfirmation -> Confirmed =====

    [Fact]
    public async Task AwaitingConfirmation_OnOrderConfirmed_TransitionsToConfirmed_AndPublishesCheckoutCompleted()
    {
        // ADR-0011 PII null-out also runs on this terminal transition, but the saga is
        // finalised before we can re-read state - that property is covered by static review of
        // CheckoutSagaOrchestrator.NullOutAddresses being chained on every TransitionTo terminal
        // and is more directly testable via an integration test post-M5.
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingConfirmation(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        var beforeState = _sagaHarness.Sagas.Contains(correlationId)!;
        beforeState.ShippingAddressJson.Should().NotBeNull("address present until terminal transition");

        await _testHarness.Bus.Publish(new OrderConfirmedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            ConfirmedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<OrderConfirmedSagaEvent>()).Should().BeTrue();

        var sagaFinalized = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var completedEvents = _fakeOutboxWriter.GetMessages<CheckoutCompletedEvent>().ToList();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("Confirmed is terminal");
            completedEvents.Should().ContainSingle();
            completedEvents[0].IntegrationEvent.OrderId.Should().Be(orderId);
        }
    }

    // ===== § 4 row 11: AwaitingConfirmation - ReservationConfirmed (informational) =====

    [Fact]
    public async Task AwaitingConfirmation_OnReservationConfirmed_StaysInState_AndPublishesNothing()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingConfirmation(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        var preState = _sagaHarness.Sagas.Contains(correlationId)!;
        var rid = GetReservationId(preState, product1);

        await _testHarness.Bus.Publish(new ReservationConfirmedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = rid,
            ConfirmedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<ReservationConfirmedSagaEvent>()).Should().BeTrue();

        var stillAwaiting = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingConfirmation);

        using (new AssertionScope())
        {
            stillAwaiting.Should().NotBeNull("ReservationConfirmed is informational; Ordering's confirm is the gate");
            _fakeOutboxWriter.CapturedMessages.Should().BeEmpty("informational event publishes nothing");
        }
    }

    // ===== § 4 row 12: AwaitingConfirmation -> CompensatingPayment (refund-first) =====

    [Fact]
    public async Task AwaitingConfirmation_OnOrderFailed_TransitionsToCompensatingPayment_AndPublishesRequestRefund()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingConfirmation(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new OrderFailedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            ErrorCode = "CONFIRMATION_FAILED",
            ErrorMessage = "Internal error confirming order",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<OrderFailedSagaEvent>()).Should().BeTrue();

        var compensatingPayment = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingPayment);
        var refunds = _fakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();

        using (new AssertionScope())
        {
            compensatingPayment.Should().NotBeNull();
            compensatingPayment.CompensationTriggered.Should().BeTrue();
            refunds.Should().ContainSingle();
            refunds[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            // Refund-first per § 6.1: stock release / cancel order happen AFTER PaymentRefunded.
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeFalse();
            _fakeOutboxWriter.HasMessage<CancelOrderCommand>().Should().BeFalse();
        }
    }

    // ===== § 4 row 13/14: CompensatingStockReservations -> Compensated =====

    [Fact]
    public async Task CompensatingStockReservations_AllReleasesPlusOrderCancelled_TransitionsToCompensated_AndPublishesCheckoutFailed()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingStockReservations(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        var stateInComp = _sagaHarness.Sagas.Contains(correlationId)!;
        var rid = GetReservationId(stateInComp, product1);

        await _testHarness.Bus.Publish(new ReservationReleasedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = rid,
            ReleaseReason = nameof(ReleaseReason.Compensation),
            ReleasedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<ReservationReleasedSagaEvent>();

        // Still waiting for OrderCancelled
        var stillCompensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);
        stillCompensating.Should().NotBeNull("OrderCancelled not yet received");

        await _testHarness.Bus.Publish(new OrderCancelledSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            CancelledAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<OrderCancelledSagaEvent>()).Should().BeTrue();

        var sagaFinalized = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var failedEvents = _fakeOutboxWriter.GetMessages<CheckoutFailedEvent>().ToList();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("Compensated is terminal");
            failedEvents.Should().ContainSingle();
            failedEvents[0].IntegrationEvent.CompensationTriggered.Should().BeTrue();
        }
    }

    // ===== § 4 row 16: CompensatingPayment -> CompensatingStockReservations =====

    [Fact]
    public async Task CompensatingPayment_OnPaymentRefunded_TransitionsToCompensatingStockReservations_AndPublishesReleaseAndCancel()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingPayment(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new PaymentRefundedSagaEvent
        {
            CorrelationId = correlationId,
            PaymentTransactionId = Guid.CreateVersion7(),
            Amount = 9.99m,
            Currency = "USD",
            RefundedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        (await _sagaHarness.Consumed.Any<PaymentRefundedSagaEvent>()).Should().BeTrue();

        var compensatingStock = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);

        using (new AssertionScope())
        {
            compensatingStock.Should().NotBeNull();
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeTrue();
            _fakeOutboxWriter.HasMessage<CancelOrderCommand>().Should().BeTrue();
        }
    }

    // ===== § 5.3 race condition: duplicate StockReserved is idempotent =====

    [Fact]
    public async Task DuplicateStockReservedForSameProduct_IsIdempotentNoOp()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId, BuildItem(product1));

        var state = _sagaHarness.Sagas.Contains(correlationId)!;
        var rid = GetReservationId(state, product1);

        var sagaEvent = new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = rid,
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        };

        await _testHarness.Bus.Publish(sagaEvent);
        await WaitForConsumed<StockReservedSagaEvent>(1);
        // duplicate delivery
        await _testHarness.Bus.Publish(sagaEvent);
        await WaitForConsumed<StockReservedSagaEvent>(2);

        // single ProductId -> first event takes us to AwaitingPayment; duplicate is no-op there.
        var awaitingPayment = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingPayment);
        awaitingPayment.Should().NotBeNull("first reserved-event transitions; duplicate is dropped");
    }

    // ===== Multi-item fan-out: partial failure end-to-end =====

    [Fact]
    public async Task MultiItemFanOut_PartialFailure_ReleasesAlreadyReservedAndDoesNotReleaseTheFailed()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();
        var product3 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId,
            BuildItem(product1), BuildItem(product2), BuildItem(product3));

        var state = _sagaHarness.Sagas.Contains(correlationId)!;
        await _testHarness.Bus.Publish(new StockReservedSagaEvent
        {
            OrderId = orderId,
            ProductId = product1,
            ReservationId = GetReservationId(state, product1),
            Quantity = 1,
            ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
        });
        await WaitForConsumed<StockReservedSagaEvent>(1);

        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new StockReservationFailedSagaEvent
        {
            OrderId = orderId,
            ProductId = product2,
            RequestedQuantity = 5,
            AvailableQuantity = 0,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });

        await _sagaHarness.Consumed.Any<StockReservationFailedSagaEvent>();

        var releases = _fakeOutboxWriter.GetMessages<ReleaseReservationCommand>().ToList();
        using (new AssertionScope())
        {
            releases.Select(r => r.IntegrationEvent.ProductId).Should().BeEquivalentTo(new[] { product1 },
                "only the previously-Reserved product1 needs releasing; product2 failed (no reservation), product3 still pending");
        }
    }

    // ===== ADR-0011 PII retention =====
    //
    // The orchestrator's NullOutAddresses helper runs in the action chain BEFORE Finalize() on
    // every terminal transition. The saga repository finalises (removes) the instance soon
    // after, so a post-publish state lookup returns null. We verify the property indirectly:
    // (a) addresses are present at every NON-terminal checkpoint observed by other tests
    //     (e.g. AwaitingConfirmation_OnOrderConfirmed_..._AndPublishesCheckoutCompleted asserts
    //     ShippingAddressJson is non-null pre-terminal);
    // (b) the orchestrator unconditionally chains .Then(NullOutAddresses) before .TransitionTo
    //     for Confirmed / Failed / Compensated;
    // (c) the static guarantee is verified by code-review.
    //
    // A mid-compensation state-snapshot assertion follows. Once OrderCancelled lands on top of
    // releases-complete, the gate triggers terminal Compensated, and we cannot capture the
    // post-NullOutAddresses, pre-Finalize state in a unit test deterministically. Integration
    // tests (M9, against the real EF repo) will assert the persisted row's address columns are
    // null after the saga finalises.

    [Fact]
    public async Task DuringCompensation_BeforeTerminalGate_AddressesArePresent()
    {
        // Verifies pre-terminal invariant: addresses must still be present until both
        // gating events arrive. Complements the post-terminal scrub (covered by integration tests).
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingStockReservations(correlationId, orderId, product1);

        var state = _sagaHarness.Sagas.Contains(correlationId);

        using (new AssertionScope())
        {
            state.Should().NotBeNull("compensation is in progress, gate not yet met");
            state!.ShippingAddressJson.Should().NotBeNullOrEmpty();
            state.BillingAddressJson.Should().NotBeNullOrEmpty();
            state.CompensationTriggered.Should().BeTrue();
        }
    }

    // ===== § 7 timeouts (M5) =====
    //
    // Test discipline (ADR-0015 "MassTransit saga scheduler - known seam"): we drive the
    // timeout-fired branches by publishing the *TimeoutExpired record directly via
    // _testHarness.Bus.Publish(...). FakeTimeProvider.Advance does NOT advance the saga
    // scheduler's clock, so attempts to wait for a real-clock fire would hang. Direct
    // publish exercises the same .Schedule() correlation rule the scheduler would dispatch
    // through. The unschedule-on-success paths are validated indirectly: M4 tests for the
    // happy paths still pass after this milestone (we did not regress them).

    [Fact]
    public async Task AwaitingOrderCreation_OnOrderCreationTimeout_TransitionsToFailed_AndPublishesCheckoutFailedAndMarkOrderFailed()
    {
        var correlationId = Guid.CreateVersion7();
        await PublishInitiated(correlationId, BuildItem(Guid.CreateVersion7()));
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new OrderCreationTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<OrderCreationTimeoutExpired>()).Should().BeTrue();

        var sagaFinalized = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var failedEvents = _fakeOutboxWriter.GetMessages<CheckoutFailedEvent>().ToList();
        var markFailedCmds = _fakeOutboxWriter.GetMessages<MarkOrderFailedCommand>().ToList();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("Failed is terminal");
            failedEvents.Should().ContainSingle();
            failedEvents[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            failedEvents[0].IntegrationEvent.ErrorCode.Should().Be("ORDER_CREATION_TIMEOUT");
            // OrderId is null in AwaitingOrderCreation - the defensive command goes out with
            // Guid.Empty + the CorrelationId for Ordering to resolve at its end (§ 3 row 4).
            markFailedCmds.Should().ContainSingle();
            markFailedCmds[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            markFailedCmds[0].IntegrationEvent.ErrorCode.Should().Be("ORDER_CREATION_TIMEOUT");
            markFailedCmds[0].IntegrationEvent.OrderId.Should().Be(Guid.Empty);
        }
    }

    [Fact]
    public async Task AwaitingStockReservation_OnStockReservationTimeout_TransitionsToCompensatingStockReservations()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingStockReservation(correlationId, orderId, BuildItem(product1));
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new StockReservationTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<StockReservationTimeoutExpired>()).Should().BeTrue();

        var compensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);
        var cancelCmds = _fakeOutboxWriter.GetMessages<CancelOrderCommand>().ToList();

        using (new AssertionScope())
        {
            compensating.Should().NotBeNull();
            compensating!.ErrorCode.Should().Be("STOCK_TIMEOUT");
            compensating.FailedAtState.Should().Be(nameof(CheckoutSagaOrchestrator.AwaitingStockReservation));
            compensating.CompensationTriggered.Should().BeTrue();
            // No StockReservedEvents have arrived yet, so no active reservations to release;
            // CancelOrderCommand still goes out (OrderId is set after OrderCreated).
            cancelCmds.Should().ContainSingle();
            cancelCmds[0].IntegrationEvent.OrderId.Should().Be(orderId);
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeFalse();
        }
    }

    [Fact]
    public async Task AwaitingPayment_OnPaymentTimeout_TransitionsToCompensatingStockReservations_AndDispatchesReleasesAndCancel()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await ReachAwaitingPayment(correlationId, orderId, product1, product2);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new PaymentTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<PaymentTimeoutExpired>()).Should().BeTrue();

        var compensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingStockReservations);
        var releases = _fakeOutboxWriter.GetMessages<ReleaseReservationCommand>().ToList();
        var cancels = _fakeOutboxWriter.GetMessages<CancelOrderCommand>().ToList();

        using (new AssertionScope())
        {
            compensating.Should().NotBeNull();
            compensating!.ErrorCode.Should().Be("PAYMENT_TIMEOUT");
            compensating.FailedAtState.Should().Be(nameof(CheckoutSagaOrchestrator.AwaitingPayment));
            compensating.CompensationTriggered.Should().BeTrue();
            releases.Should().HaveCount(2, "two reservations were active when payment timed out");
            cancels.Should().ContainSingle();
            cancels[0].IntegrationEvent.OrderId.Should().Be(orderId);
        }
    }

    [Fact]
    public async Task AwaitingConfirmation_OnOrderConfirmationTimeout_TransitionsToCompensatingPayment_AndPublishesRequestRefund()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachAwaitingConfirmation(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new OrderConfirmationTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<OrderConfirmationTimeoutExpired>()).Should().BeTrue();

        var compensating = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensatingPayment);
        var refunds = _fakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();

        using (new AssertionScope())
        {
            compensating.Should().NotBeNull();
            compensating!.ErrorCode.Should().Be("CONFIRMATION_TIMEOUT");
            compensating.FailedAtState.Should().Be(nameof(CheckoutSagaOrchestrator.AwaitingConfirmation));
            compensating.CompensationTriggered.Should().BeTrue();
            refunds.Should().ContainSingle();
            refunds[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            // Refund-first per § 6.1: releases + cancel happen AFTER PaymentRefunded.
            _fakeOutboxWriter.HasMessage<ReleaseReservationCommand>().Should().BeFalse();
            _fakeOutboxWriter.HasMessage<CancelOrderCommand>().Should().BeFalse();
        }
    }

    [Fact]
    public async Task CompensatingStockReservations_OnCompensationTimeout_TransitionsToCompensationStuck_AndPublishesCheckoutStuck()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingStockReservations(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new CompensationTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>()).Should().BeTrue();

        var sagaFinalized = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var stuckEvents = _fakeOutboxWriter.GetMessages<CheckoutStuckEvent>().ToList();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("CompensationStuck is abnormal-terminal");
            stuckEvents.Should().ContainSingle();
            stuckEvents[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            stuckEvents[0].IntegrationEvent.OrderId.Should().Be(orderId);
            stuckEvents[0].IntegrationEvent.ErrorCode.Should().Be("COMPENSATION_TIMEOUT");
            stuckEvents[0].IntegrationEvent.LastState.Should().Be(
                nameof(CheckoutSagaOrchestrator.CompensatingStockReservations));
        }
    }

    [Fact]
    public async Task CompensatingPayment_OnCompensationTimeout_TransitionsToCompensationStuck_AndPublishesCheckoutStuck()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await ReachCompensatingPayment(correlationId, orderId, product1);
        _fakeOutboxWriter.Clear();

        await _testHarness.Bus.Publish(new CompensationTimeoutExpired { CorrelationId = correlationId });

        (await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>()).Should().BeTrue();

        var sagaFinalized = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        var stuckEvents = _fakeOutboxWriter.GetMessages<CheckoutStuckEvent>().ToList();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("CompensationStuck is abnormal-terminal");
            stuckEvents.Should().ContainSingle();
            stuckEvents[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            stuckEvents[0].IntegrationEvent.OrderId.Should().Be(orderId);
            stuckEvents[0].IntegrationEvent.ErrorCode.Should().Be("COMPENSATION_TIMEOUT");
            stuckEvents[0].IntegrationEvent.LastState.Should().Be(
                nameof(CheckoutSagaOrchestrator.CompensatingPayment));
        }
    }

    // ===== helpers =====

    private async Task PublishInitiated(Guid correlationId, params CheckoutItemSnapshot[] items)
    {
        var sagaEvent = BuildBasketCheckoutInitiated(
            correlationId, Guid.CreateVersion7(), Guid.CreateVersion7(), items);
        await _testHarness.Bus.Publish(sagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();
    }

    private async Task ReachAwaitingStockReservation(
        Guid correlationId,
        Guid orderId,
        params CheckoutItemSnapshot[] items)
    {
        await PublishInitiated(correlationId, items);
        await _testHarness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<OrderCreatedSagaEvent>();
    }

    private async Task ReachAwaitingPayment(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingStockReservation(correlationId, orderId,
            productIds.Select(BuildItem).ToArray());
        var state = _sagaHarness.Sagas.Contains(correlationId)!;

        var consumed = 0;
        foreach (var productId in productIds)
        {
            await _testHarness.Bus.Publish(new StockReservedSagaEvent
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = GetReservationId(state, productId),
                Quantity = 1,
                ReservedAtUtc = _fakeTimeProvider.GetUtcNow(),
                ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddMinutes(15)
            });
            consumed++;
            await WaitForConsumed<StockReservedSagaEvent>(consumed);
        }
    }

    private async Task ReachAwaitingConfirmation(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingPayment(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new PaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            PaymentTransactionId = Guid.CreateVersion7(),
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<PaymentCompletedSagaEvent>();
    }

    private async Task ReachCompensatingStockReservations(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingPayment(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new PaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            ErrorCode = "PAYMENT_FAILED",
            ErrorMessage = "Card declined",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<PaymentFailedSagaEvent>();
    }

    private async Task ReachCompensatingPayment(Guid correlationId, Guid orderId, params Guid[] productIds)
    {
        await ReachAwaitingConfirmation(correlationId, orderId, productIds);
        await _testHarness.Bus.Publish(new OrderFailedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            ErrorCode = "CONFIRMATION_FAILED",
            ErrorMessage = "Internal Ordering error",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow()
        });
        await _sagaHarness.Consumed.Any<OrderFailedSagaEvent>();
    }

    private BasketCheckoutInitiatedSagaEvent BuildBasketCheckoutInitiated(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        params CheckoutItemSnapshot[] items)
    {
        var basketJson = JsonSerializer.Serialize(items);
        var addr = new
        {
            Street1 = "123 Test St",
            Street2 = (string?)null,
            City = "Testville",
            State = (string?)null,
            PostalCode = "12345",
            CountryCode = "US"
        };
        var addressJson = JsonSerializer.Serialize(addr);

        return new BasketCheckoutInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            BasketSnapshotJson = basketJson,
            TotalAmount = items.Sum(i => i.LineTotal),
            Currency = "USD",
            PaymentMethodId = paymentMethodId,
            ShippingAddressJson = addressJson,
            BillingAddressJson = addressJson,
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow()
        };
    }

    private static CheckoutItemSnapshot BuildItem(Guid productId, int qty = 1) =>
        new(productId, "SKU-" + productId.ToString("N")[..6], "Product", qty, 9.99m, "USD", qty * 9.99m);

    private static Guid GetReservationId(CheckoutSagaState state, Guid productId)
    {
        using var doc = JsonDocument.Parse(state.ReservationIdsJson);
        var entry = doc.RootElement.GetProperty(productId.ToString());
        return entry.GetProperty("ReservationId").GetGuid();
    }

    /// <summary>
    /// Waits until at least <paramref name="expectedCount"/> messages of type
    /// <typeparamref name="T"/> have been observed by the saga test harness, polling on a short
    /// interval. Replaces <c>SelectAsync&lt;T&gt;().Take(n).ToListAsync()</c> which is
    /// ambiguous between MassTransit's and System.Linq's <c>Take</c> on
    /// <see cref="IAsyncEnumerable{T}"/> in this test project.
    /// </summary>
    private async Task WaitForConsumed<T>(int expectedCount)
        where T : class
    {
        var deadline = DateTime.UtcNow.Add(DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var seen = 0;
            await foreach (var _ in _sagaHarness.Consumed.SelectAsync<T>(TestContext.Current.CancellationToken))
            {
                seen++;
                if (seen >= expectedCount)
                {
                    return;
                }
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for {expectedCount} {typeof(T).Name} messages; observed only some.");
    }

    /// <summary>
    /// Mirrors the JSON shape written by <c>BasketCheckoutInitiatedConsumer</c>'s
    /// internal BasketItemSnapshot record — kept as a test-local DTO so tests don't need to
    /// reach into the consumer's internals.
    /// </summary>
    private sealed record CheckoutItemSnapshot(
        Guid ProductId,
        string Sku,
        string Name,
        int Quantity,
        decimal UnitPriceAmount,
        string UnitPriceCurrency,
        decimal LineTotal);
}
