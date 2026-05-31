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
using SagaOrchestrators.UnitTests.Checkout;

namespace SagaOrchestrators.UnitTests.Sagas;

/// <summary>
/// Verifies the topology-swap flag <see cref="CheckoutSagaFeatureFlags.PaymentThenStock"/>
/// (ADR-0014, third showcase pattern) drives the post-OrderCreated branch in
/// <see cref="CheckoutSagaOrchestrator.ConfigureAwaitingOrderCreationState"/>. Default OFF
/// per ADR-0004 takes the stock-then-payment path; ON takes the experimental
/// payment-then-stock stub. ON is intentionally not validated end-to-end in v1 (ADR-0014
/// line 116) — these tests only assert that the branch is taken and that the right outbox
/// payload is published.
/// </summary>
/// <remarks>
/// Each test builds its own fixture with the desired flag value because the
/// <see cref="IFeatureClient"/> is registered as a DI singleton and we need different stubs
/// for OFF and ON. Joins <see cref="CheckoutMeterSerialCollection"/> for the same reason
/// <see cref="CheckoutSagaOrchestratorTests"/> does — serialises against the
/// process-global SagaOrchestrators meter while
/// <c>CheckoutSagaMetricsEmissionTests</c> attaches its <c>MeterListener</c>.
/// </remarks>
[Collection(nameof(CheckoutMeterSerialCollection))]
public class CheckoutSagaOrchestratorFlagTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task OrderCreated_WithFlagOff_TransitionsToAwaitingStockReservation_AndDispatchesReserveStock()
    {
        var fakeTime = new FakeTimeProvider();
        var fakeOutbox = new FakeOutboxWriter();
        await using var setup = await BuildHarnessAsync(paymentThenStockEnabled: false, fakeTime, fakeOutbox);

        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await PublishInitiated(setup, fakeTime, correlationId,
            BuildItem(product1, qty: 2), BuildItem(product2, qty: 1));
        fakeOutbox.Clear();

        await setup.Harness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = fakeTime.GetUtcNow()
        });

        (await setup.SagaHarness.Consumed.Any<OrderCreatedSagaEvent>()).Should().BeTrue();

        var state = setup.SagaHarness.Sagas.ContainsInState(
            correlationId, setup.SagaHarness.StateMachine,
            setup.SagaHarness.StateMachine.AwaitingStockReservation);
        var reserveCommands = fakeOutbox.GetMessages<ReserveStockCommand>().ToList();
        var paymentRequested = fakeOutbox.GetMessages<RequestPaymentCommand>().ToList();

        using (new AssertionScope())
        {
            state.Should().NotBeNull("OFF branch must take the stock-then-payment path (ADR-0004 default)");
            state!.OrderId.Should().Be(orderId);
            state.ExpectedReservations.Should().Be(2, "two distinct ProductIds were in the basket");
            state.PendingReservations.Should().Be(2);
            reserveCommands.Should().HaveCount(2, "OFF branch fans out one ReserveStockCommand per distinct ProductId");
            reserveCommands.Select(m => m.IntegrationEvent.ProductId).Should().BeEquivalentTo(new[] { product1, product2 });
            paymentRequested.Should().BeEmpty("OFF branch does NOT publish RequestPaymentCommand until all stock is reserved");
        }
    }

    [Fact]
    public async Task OrderCreated_WithFlagOn_TransitionsToAwaitingPayment_AndDispatchesPaymentRequested_AndDoesNotReserveStock()
    {
        var fakeTime = new FakeTimeProvider();
        var fakeOutbox = new FakeOutboxWriter();
        await using var setup = await BuildHarnessAsync(paymentThenStockEnabled: true, fakeTime, fakeOutbox);

        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();
        var product2 = Guid.CreateVersion7();

        await PublishInitiated(setup, fakeTime, correlationId, userId, paymentMethodId,
            BuildItem(product1, qty: 2), BuildItem(product2, qty: 1));
        fakeOutbox.Clear();

        await setup.Harness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = fakeTime.GetUtcNow()
        });

        (await setup.SagaHarness.Consumed.Any<OrderCreatedSagaEvent>()).Should().BeTrue();

        var state = setup.SagaHarness.Sagas.ContainsInState(
            correlationId, setup.SagaHarness.StateMachine,
            setup.SagaHarness.StateMachine.AwaitingPayment);
        var reserveCommands = fakeOutbox.GetMessages<ReserveStockCommand>().ToList();
        var paymentRequested = fakeOutbox.GetMessages<RequestPaymentCommand>().ToList();

        using (new AssertionScope())
        {
            state.Should().NotBeNull("ON branch must take the experimental payment-then-stock path (ADR-0014)");
            state!.OrderId.Should().Be(orderId);
            state.ExpectedReservations.Should().Be(0, "ON branch skips stock reservation tracking init");
            state.PendingReservations.Should().Be(0);
            state.PaymentRequestedAtUtc.Should().NotBeNull("PaymentRequestedAtUtc is set when RequestPaymentCommand is dispatched");
            reserveCommands.Should().BeEmpty("ON branch must NOT fan out ReserveStockCommand — that's the whole point of the swap");
            paymentRequested.Should().ContainSingle("ON branch dispatches RequestPaymentCommand immediately after OrderCreated");
            paymentRequested[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            paymentRequested[0].IntegrationEvent.OrderId.Should().Be(orderId);
            paymentRequested[0].IntegrationEvent.UserId.Should().Be(userId);
            // C-2 closeout: Payments wire shape is string. CheckoutSaga stringifies at the boundary.
            paymentRequested[0].IntegrationEvent.PaymentMethodId.Should().Be(paymentMethodId.ToString());
            paymentRequested[0].IntegrationEvent.IdempotencyKey.Should().Be(correlationId.ToString());
        }
    }

    /// <summary>
    /// Regression for <c>ThenAsync</c> sequencing — a future change reverting the flag-read step
    /// to <c>Then(async …)</c> would silently bind it as <c>async void</c>, the IfElse predicate
    /// would read the default <c>false</c>, and the experimental ON branch would become
    /// unreachable under any real OpenFeature provider that genuinely awaits I/O. The
    /// <see cref="CheckoutFeatureClientStub.WithPaymentThenStockAwaiting"/> stub yields once
    /// before returning so the bug surfaces deterministically; the existing OFF/ON tests use
    /// <see cref="Task.FromResult{T}(T)"/> which masks it.
    /// </summary>
    [Fact]
    public async Task OrderCreated_WithGenuinelyAsyncFlagRead_StillRoutesToOnBranch()
    {
        var fakeTime = new FakeTimeProvider();
        var fakeOutbox = new FakeOutboxWriter();
        await using var setup = await BuildHarnessAsync(
            CheckoutFeatureClientStub.WithPaymentThenStockAwaiting(paymentThenStockEnabled: true),
            fakeTime,
            fakeOutbox);

        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var product1 = Guid.CreateVersion7();

        await PublishInitiated(setup, fakeTime, correlationId, BuildItem(product1));
        fakeOutbox.Clear();

        await setup.Harness.Bus.Publish(new OrderCreatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            OrderCreatedAtUtc = fakeTime.GetUtcNow()
        });

        (await setup.SagaHarness.Consumed.Any<OrderCreatedSagaEvent>()).Should().BeTrue();

        var state = setup.SagaHarness.Sagas.ContainsInState(
            correlationId, setup.SagaHarness.StateMachine,
            setup.SagaHarness.StateMachine.AwaitingPayment);
        var paymentRequested = fakeOutbox.GetMessages<RequestPaymentCommand>().ToList();
        var reserveCommands = fakeOutbox.GetMessages<ReserveStockCommand>().ToList();

        using (new AssertionScope())
        {
            state.Should().NotBeNull(
                "even when the IFeatureClient yields before completing, the orchestrator must " +
                "AWAIT the flag read before evaluating the IfElse predicate (ThenAsync, not " +
                "Then(async ...))");
            paymentRequested.Should().ContainSingle();
            reserveCommands.Should().BeEmpty();
        }
    }

    // ----- harness/test helpers -----

    private static async Task<TestSetup> BuildHarnessAsync(
        bool paymentThenStockEnabled,
        FakeTimeProvider fakeTime,
        FakeOutboxWriter fakeOutbox) =>
        await BuildHarnessAsync(
            CheckoutFeatureClientStub.WithPaymentThenStock(paymentThenStockEnabled),
            fakeTime,
            fakeOutbox);

    private static async Task<TestSetup> BuildHarnessAsync(
        IFeatureClient featureClient,
        FakeTimeProvider fakeTime,
        FakeOutboxWriter fakeOutbox)
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        var provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<CheckoutSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(fakeTime)
            .AddSingleton<IFeatureClient>(featureClient)
            .AddSagaOutboxTestServices(testDbName, fakeOutbox)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<CheckoutSagaOrchestrator, CheckoutSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        var sagaHarness = harness.GetSagaStateMachineHarness<CheckoutSagaOrchestrator, CheckoutSagaState>();
        await harness.Start();
        return new TestSetup(provider, harness, sagaHarness);
    }

    private static Task PublishInitiated(
        TestSetup setup,
        FakeTimeProvider fakeTime,
        Guid correlationId,
        params CheckoutItemSnapshot[] items) =>
        PublishInitiated(setup, fakeTime, correlationId, Guid.CreateVersion7(), Guid.CreateVersion7(), items);

    private static async Task PublishInitiated(
        TestSetup setup,
        FakeTimeProvider fakeTime,
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        params CheckoutItemSnapshot[] items)
    {
        var sagaEvent = BuildBasketCheckoutInitiated(fakeTime, correlationId, userId, paymentMethodId, items);
        await setup.Harness.Bus.Publish(sagaEvent);
        var sagaExists = await setup.SagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();
    }

    private static BasketCheckoutInitiatedSagaEvent BuildBasketCheckoutInitiated(
        FakeTimeProvider fakeTime,
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        CheckoutItemSnapshot[] items)
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
            InitiatedAtUtc = fakeTime.GetUtcNow()
        };
    }

    private static CheckoutItemSnapshot BuildItem(Guid productId, int qty = 1) =>
        new(productId, "SKU-" + productId.ToString("N")[..6], "Product", qty, 9.99m, "USD", qty * 9.99m);

    /// <summary>
    /// Minimal harness-disposable wrapper. Stops the bus and disposes the provider in
    /// LIFO order so MassTransit's Stop semantics complete before DI tear-down.
    /// </summary>
    private sealed record TestSetup(
        ServiceProvider Provider,
        ITestHarness Harness,
        ISagaStateMachineTestHarness<CheckoutSagaOrchestrator, CheckoutSagaState> SagaHarness)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>
    /// Mirrors the JSON shape written by <c>BasketCheckoutInitiatedConsumer</c>'s internal
    /// BasketItemSnapshot record — kept as a test-local DTO so tests don't reach into consumer
    /// internals. Same shape as the helper in <see cref="CheckoutSagaOrchestratorTests"/>.
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
