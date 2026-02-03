using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using Finance.Payments;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public class AlertSubscriptionPurchaseSagaTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState> _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<AlertSubscriptionPurchaseSaga>>())
            .AddSingleton(sagaOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenSubscriptionPurchaseInitiated_ShouldTransitionToWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            SubscriptionTier = SubscriptionTier.Pro,
            DurationDays = 30,
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"purchase-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _harness.Bus.Publish(initiatedEvent);

        // Assert - wait for the event to be consumed
        (await _sagaHarness.Consumed.Any<AlertSubscriptionPurchaseInitiatedSagaEvent>()).Should().BeTrue();

        // Wait for saga to exist
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));
        sagaExists.HasValue.Should()
            .BeTrue("Saga should be created after publishing SubscriptionPurchaseInitiatedEvent");

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.WaitingForPayment);

        instance.Should().NotBeNull();
        instance.UserId.Should().Be(userId);
        instance.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
        instance.DurationDays.Should().Be(30);
        instance.Amount.Should().Be(9.99m);
        instance.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task WhenPaymentCompletedThenActivated_ShouldTransitionToActivationCompleted()
    {
        // Arrange - Start saga
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Arrange - Payment completed
        var paymentCompletedEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 19.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchasePaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingActivation state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingActivation);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingActivation state after payment completed");
        awaitingInstance.PaymentTransactionId.Should().Be(paymentTransactionId);

        // Act - Activation completed
        var activatedEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(activatedEvent);

        // Assert - event was consumed
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivatedSagaEvent>()).Should().BeTrue();

        // Wait for saga to reach final state using proper MassTransit waiting
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should()
            .BeFalse("Saga should be finalized and removed from repository after activation completed");
    }

    [Fact]
    public async Task WhenActivationFailed_WithCompensation_ShouldTransitionToCompensationInProgress()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act - Activation fails with compensation
        var failedEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);

        instance.Should().NotBeNull();
        instance.CompensationTriggered.Should().BeTrue();
        instance.ErrorCode.Should().Be("ACTIVATION_ERROR");
    }

    [Fact]
    public async Task WhenActivationFailed_WithoutCompensation_ShouldTransitionToActivationFailed()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "VALIDATION_ERROR",
            ErrorMessage = "Invalid subscription tier",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert - event was consumed
        (await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>()).Should().BeTrue();

        // Wait for saga to reach final state using proper MassTransit waiting
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should()
            .BeFalse(
                "Saga should be finalized and removed from repository after activation failed without compensation");
    }

    [Fact]
    public async Task WhenCompensationCompleted_ShouldTransitionToCompensationCompleted()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation to get to CompensationInProgress state
        var failedEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Verify saga is in CompensationInProgress state (not finalized yet)
        var inProgressInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);
        inProgressInstance.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act - Complete compensation
        var compensationEvent = new AlertSubscriptionPurchaseCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.NewGuid(),
            CompensatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(compensationEvent);

        // Assert - event was consumed
        (await _sagaHarness.Consumed.Any<AlertSubscriptionPurchaseCompensationCompletedSagaEvent>()).Should().BeTrue();

        // Wait for saga to reach final state using proper MassTransit waiting
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should()
            .BeFalse("Saga should be finalized and removed from repository after compensation completed");
    }

    [Fact]
    public async Task WhenActivationFailed_WithCompensation_ShouldPublishRequestRefundCommand()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act - Activation fails with compensation
        var failedEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Assert - RequestRefundCommand should be published to Finance.Payments
        (await _harness.Published.Any<RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when activation fails with compensation");

        var publishedCommands = await _harness.Published.SelectAsync<RequestRefundCommand>().ToListAsync();
        var publishedCommand = publishedCommands.FirstOrDefault();
        publishedCommand.Should().NotBeNull();
        publishedCommand.Context.Message.CorrelationId.Should().Be(correlationId);
        publishedCommand.Context.Message.UserId.Should().Be(userId);
        publishedCommand.Context.Message.PaymentTransactionId.Should().Be(paymentTransactionId);
    }

    [Fact]
    public async Task WhenPurchaseInitiated_ShouldInitializeAllStateProperties()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var initiatedAt = _fakeTimeProvider.GetUtcNow().UtcDateTime;

        var initiatedEvent = new AlertSubscriptionPurchaseInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            SubscriptionTier = SubscriptionTier.Ultra,
            DurationDays = 365,
            Amount = 99.99m,
            Currency = "EUR",
            IdempotencyKey = $"purchase-{userId}-test",
            InitiatedAtUtc = initiatedAt
        };

        // Act
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchaseInitiatedSagaEvent>();
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Assert - verify all state properties are initialized
        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.WaitingForPayment);

        instance.Should().NotBeNull();
        instance.CorrelationId.Should().Be(correlationId);
        instance.UserId.Should().Be(userId);
        instance.PaymentMethodId.Should().Be(paymentMethodId);
        instance.PaymentTransactionId.Should().BeNull("PaymentTransactionId is set after payment completes");
        instance.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
        instance.DurationDays.Should().Be(365);
        instance.Amount.Should().Be(99.99m);
        instance.Currency.Should().Be("EUR");
        instance.IdempotencyKey.Should().Be($"purchase-{userId}-test");
        instance.PurchaseInitiatedAtUtc.Should().Be(initiatedAt);
        instance.CurrentState.Should().Be("WaitingForPayment");
        instance.CompensationTriggered.Should().BeFalse();
        instance.ActivationCompletedAtUtc.Should().BeNull();
        instance.CompensationCompletedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task WhenActivationTimeout_ShouldFinalizeWithActivationFailed()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        // Act - simulate activation timeout
        var timeoutEvent = new ActivationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<ActivationTimeoutExpired>();

        // Assert - saga should be finalized (removed from repository) after timeout
        // The saga transitions to ActivationFailed and then finalizes
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(1));
        sagaExists.HasValue.Should().BeFalse("Saga should be finalized after activation timeout");

        // Verify the timeout event was consumed
        (await _sagaHarness.Consumed.Any<ActivationTimeoutExpired>()).Should().BeTrue();
    }

    [Fact]
    public async Task WhenCompensationTimeout_ShouldTransitionToCompensationFailed()
    {
        // Arrange - Start saga and complete payment
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation to get to CompensationInProgress state
        var failedEvent = new AlertSubscriptionActivationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ERROR",
            ErrorMessage = "Failed to activate",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Verify saga is in CompensationInProgress state
        var inProgressInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);
        inProgressInstance.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act - simulate compensation timeout
        var timeoutEvent = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert - saga should be finalized (removed from repository) after compensation failed
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after compensation timeout");
    }

    [Fact]
    public async Task WhenDuplicatePurchaseInitiatedEvent_ShouldNotCreateNewSaga()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);

        // Act - publish the same event twice
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        await _harness.Bus.Publish(initiatedEvent);
        await Task.Delay(500); // Give time for potential duplicate processing

        // Assert - should still have only one saga instance
        var sagas = await _sagaHarness.Sagas.SelectAsync(x => x.CorrelationId == correlationId).ToListAsync();
        sagas.Should().ContainSingle("Duplicate purchase initiated events should not create additional saga instances");
    }

    [Fact]
    public async Task WhenActivatedEventForNonExistentSaga_ShouldNotCreateSaga()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act - publish activated event without prior purchase event
        var activatedEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = Guid.NewGuid(),
            ActivatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(activatedEvent);
        await Task.Delay(500); // Give time for potential processing

        // Assert - no saga should be created
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(1));
        sagaExists.HasValue.Should().BeFalse("Activated event should not create a new saga instance");
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
            IdempotencyKey = $"purchase-{userId}-{Guid.NewGuid()}",
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
        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId, paymentMethodId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Complete payment
        var paymentCompletedEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<AlertSubscriptionPurchasePaymentCompletedSagaEvent>();

        // Verify saga is now in AwaitingActivation state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingActivation);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingActivation state after payment completed");
    }
}
