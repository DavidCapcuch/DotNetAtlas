using Finance.Payments;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Order.AlertSubscriptions;
using Platform.Test.Framework.Kafka;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Schedules;

namespace SagaOrchestrators.UnitTests.Sagas;

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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
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
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);

        // Assert
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
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

        var alertSubscriptionExtensionInitiatedSagaEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Arrange - Payment completed
        var alertSubscriptionExtensionPaymentCompletedSagaEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionPaymentCompletedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingExtensionSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingExtension);
        awaitingExtensionSagaState.Should().NotBeNull("Saga should be in AwaitingExtension state");
        awaitingExtensionSagaState.PaymentTransactionId.Should().Be(paymentTransactionId);

        // Act - Extension completed
        var alertSubscriptionExtendedSagaEvent = new AlertSubscriptionExtendedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 30,
            NewExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(30).UtcDateTime,
            ExtendedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtendedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtendedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized");

        var completedMessages = _fakeOutboxWriter.GetMessages<AlertSubscriptionExtensionCompletedEvent>().ToList();
        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<AlertSubscriptionExtensionCompletedEvent>().Should().BeTrue(
                "AlertSubscriptionExtensionCompletedEvent should be added to the outbox when extension completes");
            completedMessages.Should().ContainSingle();
            completedMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            completedMessages[0].IntegrationEvent.UserId.Should().Be(userId);
        }
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
        var alertSubscriptionExtensionFailedSagaEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>()).Should().BeTrue();

        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);

        using (new AssertionScope())
        {
            compensationInProgressSagaState.Should().NotBeNull();
            compensationInProgressSagaState.CompensationTriggered.Should().BeTrue();
            compensationInProgressSagaState.ErrorCode.Should().Be("EXTENSION_ERROR");
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
        var alertSubscriptionExtensionFailedSagaEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "VALIDATION_ERROR",
            ErrorMessage = "Invalid duration",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized");

        var failedMessages = _fakeOutboxWriter.GetMessages<AlertSubscriptionExtensionFailedEvent>().ToList();
        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<AlertSubscriptionExtensionFailedEvent>().Should().BeTrue(
                "AlertSubscriptionExtensionFailedEvent should be added to the outbox when extension fails without compensation");
            failedMessages.Should().ContainSingle();
            failedMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            failedMessages[0].IntegrationEvent.CompensationTriggered.Should().BeFalse();
        }
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
        var alertSubscriptionExtensionFailedSagaEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionFailedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);
        compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act
        var alertSubscriptionExtensionCompensationCompletedSagaEvent = new AlertSubscriptionExtensionCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            RefundTransactionId = Guid.CreateVersion7(),
            CompensatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionCompensationCompletedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionCompensationCompletedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized");

        var failedMessages = _fakeOutboxWriter.GetMessages<AlertSubscriptionExtensionFailedEvent>().ToList();
        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<AlertSubscriptionExtensionFailedEvent>().Should().BeTrue(
                "AlertSubscriptionExtensionFailedEvent should be added to the outbox after compensation completed");
            failedMessages.Should().ContainSingle();
            failedMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            failedMessages[0].IntegrationEvent.CompensationTriggered.Should().BeTrue();
        }
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
        var alertSubscriptionExtensionFailedSagaEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionFailedSagaEvent);
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

        var alertSubscriptionExtensionInitiatedSagaEvent = new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = 365,
            Amount = 99.99m,
            Currency = "EUR",
            InitiatedAtUtc = initiatedAt
        };

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Assert
        var waitingForPaymentSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.WaitingForPayment);

        using (new AssertionScope())
        {
            waitingForPaymentSagaState.Should().NotBeNull();
            waitingForPaymentSagaState.CorrelationId.Should().Be(correlationId);
            waitingForPaymentSagaState.UserId.Should().Be(userId);
            waitingForPaymentSagaState.PaymentMethodId.Should().Be(paymentMethodId);
            waitingForPaymentSagaState.PaymentTransactionId.Should().BeNull("PaymentTransactionId is set after payment completes");
            waitingForPaymentSagaState.DurationDays.Should().Be(365);
            waitingForPaymentSagaState.Amount.Should().Be(99.99m);
            waitingForPaymentSagaState.Currency.Should().Be("EUR");
            waitingForPaymentSagaState.ExtensionInitiatedAtUtc.Should().Be(initiatedAt);
            waitingForPaymentSagaState.CurrentState.Should().Be("WaitingForPayment");
            waitingForPaymentSagaState.CompensationTriggered.Should().BeFalse();
            waitingForPaymentSagaState.ExtensionCompletedAtUtc.Should().BeNull();
            waitingForPaymentSagaState.CompensationCompletedAtUtc.Should().BeNull();
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
        var extensionTimeoutExpired = new ExtensionTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(extensionTimeoutExpired);
        await _sagaHarness.Consumed.Any<ExtensionTimeoutExpired>();

        // Assert - saga should trigger compensation after extension timeout
        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);

        using (new AssertionScope())
        {
            compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state after extension timeout");
            compensationInProgressSagaState.CompensationTriggered.Should().BeTrue();
            compensationInProgressSagaState.ErrorCode.Should().Be("EXTENSION_TIMEOUT");
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
        var alertSubscriptionExtensionFailedSagaEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionFailedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);
        compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act
        var compensationTimeoutExpired = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(compensationTimeoutExpired);
        await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert
        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after compensation timeout");

        var failedMessages = _fakeOutboxWriter.GetMessages<AlertSubscriptionExtensionFailedEvent>().ToList();
        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<AlertSubscriptionExtensionFailedEvent>().Should().BeTrue(
                "AlertSubscriptionExtensionFailedEvent should be added to the outbox after compensation timeout");
            failedMessages.Should().ContainSingle();
            failedMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            failedMessages[0].IntegrationEvent.CompensationTriggered.Should().BeTrue();
            failedMessages[0].IntegrationEvent.ErrorCode.Should().Be("COMPENSATION_TIMEOUT");
        }
    }

    [Fact]
    public async Task WhenPaymentFailed_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionExtensionInitiatedSagaEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var alertSubscriptionExtensionPaymentFailedSagaEvent = new AlertSubscriptionExtensionPaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "INSUFFICIENT_FUNDS",
            ErrorMessage = "Insufficient funds",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionPaymentFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after payment failed");
    }

    [Fact]
    public async Task WhenPaymentTimeout_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionExtensionInitiatedSagaEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var paymentTimeoutExpired = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(paymentTimeoutExpired);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentTimeoutExpired>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after payment timeout");
    }

    [Fact]
    public async Task WhenDuplicateExtensionInitiatedEvent_ShouldNotCreateNewSaga()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionExtensionInitiatedSagaEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
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
        var alertSubscriptionExtensionInitiatedSagaEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionExtensionInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var alertSubscriptionExtensionPaymentCompletedSagaEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionExtensionPaymentCompletedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingExtensionSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingExtension);

        awaitingExtensionSagaState.Should().NotBeNull("Saga should be in AwaitingExtension state");
    }
}
