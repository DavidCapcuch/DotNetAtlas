using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Platform.Test.Framework.Kafka;
using SagaOrchestrators.Checkout.CheckoutSaga;

namespace SagaOrchestrators.UnitTests.Sagas;

/// <summary>
/// Structural unit tests for the Checkout saga state machine at milestone M2.
/// </summary>
/// <remarks>
/// M2 ships the state class (M1), the 12 internal saga event records, and the correlation
/// rules in <c>ConfigureEvents()</c>. State transitions (<c>Initially</c> / <c>During</c>)
/// land in M4; schedules in M5; consumer adapters in M3. These tests therefore assert only
/// that the orchestrator constructs cleanly under the MassTransit test harness and that every
/// declared <c>State</c> + <c>Event&lt;T&gt;</c> property is wired - smoke coverage that
/// catches regressions where a future edit drops a state declaration or forgets to call
/// <c>Event(() =&gt; X, ...)</c>. Behavioural transition coverage arrives in M4 alongside the
/// transition table implementation.
/// </remarks>
public class CheckoutSagaOrchestratorTests : IAsyncLifetime
{
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

    [Fact]
    public void Constructor_ShouldDeclareAllExplicitStates()
    {
        // Arrange
        var stateMachine = _sagaHarness.StateMachine;

        // Assert - the 10 explicit states (Initial is MassTransit-implicit).
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
        // Arrange
        var stateMachine = _sagaHarness.StateMachine;

        // Assert - one Event<T> per external event the saga consumes (checkout-saga.md § 8 table).
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
}
