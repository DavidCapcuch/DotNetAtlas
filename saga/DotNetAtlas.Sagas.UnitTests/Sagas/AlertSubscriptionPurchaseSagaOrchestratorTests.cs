using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using DotNetAtlas.Test.Framework.Kafka;
using Finance.Payments;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Sagas.UnitTests.Sagas;

/// <summary>
/// Unit tests for the SubscriptionPurchaseSaga state machine.
/// Tests verify correct state transitions, event handling, timeout scenarios, and compensation logic.
/// </summary>
/// <remarks>
/// The saga flow is:
/// 1. SubscriptionPurchaseInitiatedEvent → WaitingForPayment
/// 2. PaymentCompletedEvent → AwaitingActivation (publishes ActivateSubscriptionCommand)
/// 3. SubscriptionActivatedEvent → ActivationCompleted → Finalize
/// OR:
/// 3. SubscriptionActivationFailedEvent (with ShouldCompensate=true) → CompensationInProgress (publishes RequestRefundCommand).
/// </remarks>
public class AlertSubscriptionPurchaseSagaOrchestratorTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _testHarness = null!;

    private ISagaStateMachineTestHarness<AlertSubscriptionPurchaseSagaOrchestrator, AlertSubscriptionPurchaseSagaState>
        _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<AlertSubscriptionPurchaseSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddSagaOutboxTestServices(testDbName, _fakeOutboxWriter)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSagaOrchestrator, AlertSubscriptionPurchaseSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _testHarness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _testHarness
            .GetSagaStateMachineHarness<AlertSubscriptionPurchaseSagaOrchestrator,
                AlertSubscriptionPurchaseSagaState>();
        await _testHarness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _testHarness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenSubscriptionPurchaseInitiated_ShouldTransitionToWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionPurchaseInitiatedSagaEvent = new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            SubscriptionTier = SubscriptionTier.Pro,
            DurationDays = 30,
            Amount = 9.99m,
            Currency = "USD",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);

        // Assert
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var waitingForPaymentSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.WaitingForPayment);

        using (new AssertionScope())
        {
            waitingForPaymentSagaState.Should().NotBeNull();
            waitingForPaymentSagaState.UserId.Should().Be(userId);
            waitingForPaymentSagaState.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            waitingForPaymentSagaState.DurationDays.Should().Be(30);
            waitingForPaymentSagaState.Amount.Should().Be(9.99m);
            waitingForPaymentSagaState.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task WhenPaymentCompletedThenActivated_ShouldTransitionToActivationCompleted()
    {
        // Arrange - Start saga
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        var alertSubscriptionPurchaseInitiatedSagaEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Arrange - Payment completed
        var alertSubscriptionPurchasePaymentCompletedSagaEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 19.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionPurchasePaymentCompletedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchasePaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingActivation state
        var awaitingActivationSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingActivation);
        awaitingActivationSagaState.Should().NotBeNull("Saga should be in AwaitingActivation state after payment completed");
        awaitingActivationSagaState.PaymentTransactionId.Should().Be(paymentTransactionId);

        // Act - Activation completed
        var alertSubscriptionActivatedSagaEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivatedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivatedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized and removed from repository after activation completed");
    }

    [Fact]
    public async Task WhenActivationFailed_WithCompensation_ShouldTransitionToCompensationInProgress()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act - Activation fails with compensation
        var alertSubscriptionActivationFailedSagaEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivationFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>()).Should().BeTrue();

        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);

        using (new AssertionScope())
        {
            compensationInProgressSagaState.Should().NotBeNull();
            compensationInProgressSagaState.CompensationTriggered.Should().BeTrue();
            compensationInProgressSagaState.ErrorCode.Should().Be("ACTIVATION_ERROR");
        }
    }

    [Fact]
    public async Task WhenActivationFailed_WithoutCompensation_ShouldTransitionToActivationFailed()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var alertSubscriptionActivationFailedSagaEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "VALIDATION_ERROR",
            ErrorMessage = "Invalid subscription tier",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivationFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after activation failed without compensation");
    }

    [Fact]
    public async Task WhenCompensationCompleted_ShouldTransitionToCompensationCompleted()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation to get to CompensationInProgress state
        var alertSubscriptionActivationFailedSagaEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivationFailedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Verify saga is in CompensationInProgress state (not finalized yet)
        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);
        compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act - Complete compensation
        var alertSubscriptionPurchaseCompensationCompletedSagaEvent = new AlertSubscriptionPurchaseCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            CompensatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionPurchaseCompensationCompletedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionPurchaseCompensationCompletedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after compensation completed");
    }

    [Fact]
    public async Task WhenActivationFailed_WithCompensation_ShouldPublishRequestRefundCommand()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act - Activation fails with compensation
        var alertSubscriptionActivationFailedSagaEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivationFailedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Assert - verify message was added to the transactional outbox
        var outboxMessages = _fakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();

        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when activation fails with compensation");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.UserId.Should().Be(userId);
            outboxMessages[0].IntegrationEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
        }
    }

    [Fact]
    public async Task WhenPurchaseInitiated_ShouldInitializeAllStateProperties()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var initiatedAt = _fakeTimeProvider.GetUtcNow().UtcDateTime;

        var alertSubscriptionPurchaseInitiatedSagaEvent = new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            SubscriptionTier = SubscriptionTier.Ultra,
            DurationDays = 365,
            Amount = 99.99m,
            Currency = "EUR",
            InitiatedAtUtc = initiatedAt
        };

        // Act
        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchaseInitiatedSagaEvent>();
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
            waitingForPaymentSagaState.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
            waitingForPaymentSagaState.DurationDays.Should().Be(365);
            waitingForPaymentSagaState.Amount.Should().Be(99.99m);
            waitingForPaymentSagaState.Currency.Should().Be("EUR");
            waitingForPaymentSagaState.PurchaseInitiatedUtc.Should().Be(initiatedAt);
            waitingForPaymentSagaState.CurrentState.Should().Be("WaitingForPayment");
            waitingForPaymentSagaState.CompensationTriggered.Should().BeFalse();
            waitingForPaymentSagaState.ActivationCompletedUtc.Should().BeNull();
            waitingForPaymentSagaState.CompensationCompletedUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task WhenActivationTimeout_ShouldTransitionToCompensationInProgress()
    {
        // Arrange - Start saga and complete payment to get to AwaitingActivation state
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act - simulate activation timeout
        var activationTimeoutExpired = new ActivationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(activationTimeoutExpired);
        await _sagaHarness.Consumed.Any<ActivationTimeoutExpired>();

        // Assert
        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);

        // Verify RequestRefundCommand was added to the outbox
        var outboxMessages = _fakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();

        using (new AssertionScope())
        {
            compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state after activation timeout");
            compensationInProgressSagaState.CompensationTriggered.Should().BeTrue();
            compensationInProgressSagaState.ErrorCode.Should().Be("ACTIVATION_TIMEOUT");

            _fakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox after activation timeout");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.UserId.Should().Be(userId);
            outboxMessages[0].IntegrationEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
        }
    }

    [Fact]
    public async Task WhenCompensationTimeout_ShouldTransitionToCompensationFailed()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation to get to CompensationInProgress state
        var alertSubscriptionActivationFailedSagaEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivationFailedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Verify saga is in CompensationInProgress state
        var compensationInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.CompensationInProgress);
        compensationInProgressSagaState.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act - simulate compensation timeout
        var compensationTimeoutExpired = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(compensationTimeoutExpired);
        await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert
        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after compensation timeout");
    }

    [Fact]
    public async Task WhenDuplicatePurchaseInitiatedEvent_ShouldNotCreateNewSaga()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();

        var alertSubscriptionPurchaseInitiatedSagaEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);

        // Act - publish the same event twice
        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);
        await Task.Delay(500); // Give time for potential duplicate processing

        // Assert - should still have only one saga instance
        var sagas = await _sagaHarness.Sagas.SelectAsync(x => x.CorrelationId == correlationId).ToListAsync();
        sagas.Should().ContainSingle("Duplicate purchase initiated events should not create additional saga instances");
    }

    [Fact]
    public async Task WhenActivatedEventForNonExistentSaga_ShouldNotCreateSaga()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Act - publish activated event without prior purchase event
        var alertSubscriptionActivatedSagaEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = Guid.CreateVersion7(),
            ActivatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionActivatedSagaEvent);
        await Task.Delay(500); // Give time for potential processing

        // Assert - no saga should be created
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(1)) is not null;
        sagaExists.Should().BeFalse("Activated event should not create a new saga instance");
    }

    // -- Helper Methods --

    private AlertSubscriptionPurchaseInitiatedSagaEvent CreatePurchaseInitiatedEvent(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        SubscriptionTier tier = SubscriptionTier.Pro,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            SubscriptionTier = tier,
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
        var alertSubscriptionPurchaseInitiatedSagaEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);
        await _testHarness.Bus.Publish(alertSubscriptionPurchaseInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Complete payment
        var alertSubscriptionPurchasePaymentCompletedSagaEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(alertSubscriptionPurchasePaymentCompletedSagaEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchasePaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingActivation state
        var awaitingActivationSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingActivation);
        awaitingActivationSagaState.Should().NotBeNull("Saga should be in AwaitingActivation state after payment completed");
    }
}
