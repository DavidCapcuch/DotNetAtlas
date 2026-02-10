using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using DotNetAtlas.Test.Framework.Kafka;
using Finance.Payments;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DotNetAtlas.Sagas.UnitTests.Sagas;

/// <summary>
/// Unit tests for the SubscriptionExtensionSaga state machine.
/// Tests verify correct state transitions, event handling, timeout scenarios, and compensation logic.
/// </summary>
/// <remarks>
/// The saga flow is:
/// 1. SubscriptionExtensionInitiatedEvent → WaitingForPayment
/// 2. PaymentCompletedEvent → AwaitingExtension (publishes ExtendSubscriptionCommand)
/// 3. SubscriptionExtendedEvent → ExtensionCompleted → Finalize
/// OR:
/// 3. SubscriptionExtensionFailedEvent (with ShouldCompensate=true) → CompensationInProgress (publishes RequestRefundCommand).
/// </remarks>
public class AlertSubscriptionExtensionSagaOrchestratorTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(5);
    private ServiceProvider _provider = null!;
    private ITestHarness _testHarness = null!;

    private ISagaStateMachineTestHarness<AlertSubscriptionExtensionSagaOrchestrator,
            AlertSubscriptionExtensionSagaState>
        _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<AlertSubscriptionExtensionSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddSagaOutboxTestServices(testDbName, _fakeOutboxWriter)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<AlertSubscriptionExtensionSagaOrchestrator,
                        AlertSubscriptionExtensionSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _testHarness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _testHarness
            .GetSagaStateMachineHarness<AlertSubscriptionExtensionSagaOrchestrator,
                AlertSubscriptionExtensionSagaState>();
        await _testHarness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _testHarness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenSubscriptionExtensionInitiated_ShouldTransitionToWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionExtensionInitiatedSagaEvent = new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = 30,
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"extension-{userId}-{Guid.CreateVersion7()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);

        // Assert
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var waitingForPaymentSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.WaitingForPayment);

        using (new AssertionScope())
        {
            waitingForPaymentSagaState.Should().NotBeNull();
            waitingForPaymentSagaState.UserId.Should().Be(userId);
            waitingForPaymentSagaState.DurationDays.Should().Be(30);
            waitingForPaymentSagaState.Amount.Should().Be(9.99m);
            waitingForPaymentSagaState.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task WhenPaymentCompletedThenExtended_ShouldTransitionToExtensionCompleted()
    {
        // Arrange - Start saga
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        // Arrange - Payment completed
        var paymentCompletedEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingExtension);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingExtension state");
        awaitingInstance.PaymentTransactionId.Should().Be(paymentTransactionId);

        // Act - Extension completed
        var extendedEvent = new AlertSubscriptionExtendedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 30,
            NewExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(30).UtcDateTime,
            ExtendedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(extendedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtendedSagaEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenExtensionFailed_WithCompensation_ShouldTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);

        using (new AssertionScope())
        {
            instance.Should().NotBeNull();
            instance.CompensationTriggered.Should().BeTrue();
            instance.ErrorCode.Should().Be("EXTENSION_ERROR");
        }
    }

    [Fact]
    public async Task WhenExtensionFailed_WithoutCompensation_ShouldTransitionToExtensionFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "VALIDATION_ERROR",
            ErrorMessage = "Invalid duration",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await _testHarness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenCompensationCompleted_ShouldTransitionToCompensationCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation
        var failedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        var inProgressInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);
        inProgressInstance.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act
        var compensationEvent = new AlertSubscriptionExtensionCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            RefundTransactionId = Guid.CreateVersion7(),
            CompensatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(compensationEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionCompensationCompletedSagaEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenExtensionFailed_WithCompensation_ShouldPublishRequestRefundCommand()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        // Assert - verify message was added to the transactional outbox
        var outboxMessages = _fakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();

        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when extension fails with compensation");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.UserId.Should().Be(userId);
            outboxMessages[0].IntegrationEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
        }
    }

    [Fact]
    public async Task WhenExtensionInitiated_ShouldInitializeAllStateProperties()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var initiatedAt = _fakeTimeProvider.GetUtcNow().UtcDateTime;

        var initiatedEvent = new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = 365,
            Amount = 99.99m,
            Currency = "EUR",
            IdempotencyKey = $"extension-{userId}-test",
            InitiatedAtUtc = initiatedAt
        };

        // Act
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        // Assert
        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.WaitingForPayment);

        using (new AssertionScope())
        {
            instance.Should().NotBeNull();
            instance.CorrelationId.Should().Be(correlationId);
            instance.UserId.Should().Be(userId);
            instance.PaymentMethodId.Should().Be(paymentMethodId);
            instance.PaymentTransactionId.Should().BeNull("PaymentTransactionId is set after payment completes");
            instance.DurationDays.Should().Be(365);
            instance.Amount.Should().Be(99.99m);
            instance.Currency.Should().Be("EUR");
            instance.IdempotencyKey.Should().Be($"extension-{userId}-test");
            instance.ExtensionInitiatedAtUtc.Should().Be(initiatedAt);
            instance.CurrentState.Should().Be("WaitingForPayment");
            instance.CompensationTriggered.Should().BeFalse();
            instance.ExtensionCompletedAtUtc.Should().BeNull();
            instance.CompensationCompletedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task WhenExtensionTimeout_ShouldTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var timeoutEvent = new ExtensionTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<ExtensionTimeoutExpired>();

        // Assert - saga should trigger compensation after extension timeout
        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);

        using (new AssertionScope())
        {
            instance.Should().NotBeNull("Saga should be in CompensationInProgress state after extension timeout");
            instance.CompensationTriggered.Should().BeTrue();
            instance.ErrorCode.Should().Be("EXTENSION_TIMEOUT");
        }
    }

    [Fact]
    public async Task WhenCompensationTimeout_ShouldTransitionToCompensationFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation
        var failedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        var inProgressInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);
        inProgressInstance.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act
        var timeoutEvent = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized after compensation timeout");
    }

    [Fact]
    public async Task WhenPaymentFailed_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        // Act
        var paymentFailedEvent = new AlertSubscriptionExtensionPaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "INSUFFICIENT_FUNDS",
            ErrorMessage = "Insufficient funds",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentFailedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentFailedSagaEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized after payment failed");
    }

    [Fact]
    public async Task WhenPaymentTimeout_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        // Act
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(timeoutEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentTimeoutExpired>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: _defaultTimeout);
        finalState.HasValue.Should().BeFalse("Saga should be finalized after payment timeout");
    }

    [Fact]
    public async Task WhenDuplicateExtensionInitiatedEvent_ShouldNotCreateNewSaga()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);

        // Act
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        await _testHarness.Bus.Publish(initiatedEvent);
        await Task.Delay(500);

        // Assert
        var sagas = await _sagaHarness.Sagas.SelectAsync(x => x.CorrelationId == correlationId).ToListAsync();
        sagas.Should().ContainSingle("Duplicate events should not create additional saga instances");
    }

    // -- Helper Methods --

    private AlertSubscriptionExtensionInitiatedSagaEvent CreateExtensionInitiatedEvent(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = durationDays,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"extension-{userId}-{Guid.CreateVersion7()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };
    }

    private async Task PublishAndWaitForPaymentCompleted(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        Guid paymentTransactionId)
    {
        // Start saga
        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: _defaultTimeout);

        var paymentCompletedEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingExtension);

        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingExtension state");
    }
}
