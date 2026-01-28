using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Schedules;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public class SubscriptionExtensionSagaTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<SubscriptionExtensionSaga, SubscriptionExtensionSagaState> _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = Options.Create(new SagaOptions
        {
            MaxRetryAttempts = 3,
            RetryDelaySeconds = 5,
            ConcurrencyLimit = 10,
            KafkaBootstrapServers = "localhost:9092",
            SchemaRegistryUrl = "http://localhost:8081",
            SubscriptionTimeouts = new SubscriptionSagaTimeoutOptions
            {
                PaymentMinutes = 5,
                ActivationMinutes = 5,
                CompensationMinutes = 30
            },
            PaymentTimeouts = new PaymentSagaTimeoutOptions
            {
                AuthorizationMinutes = 5,
                CaptureMinutes = 5,
                VoidMinutes = 5,
                ActivationMinutes = 5,
                RefundMinutes = 30
            },
            Topics = new SagaTopicsOptions
            {
                OrderAlertSubscriptions = "order.alert-subscriptions",
                WeatherAlerts = "weather.alerts",
                FinancePayments = "finance.payments",
                FinancePaymentCommands = "finance.payment-commands",
                WeatherAlertsCommands = "weather.alerts.commands"
            }
        });

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<SubscriptionExtensionSaga>>())
            .AddSingleton(sagaOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<SubscriptionExtensionSaga, SubscriptionExtensionSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<SubscriptionExtensionSaga, SubscriptionExtensionSagaState>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenSubscriptionExtensionInitiated_ShouldTransitionToWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = new SubscriptionExtensionInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = 30,
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"extension-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _harness.Bus.Publish(initiatedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<SubscriptionExtensionInitiatedEvent>()).Should().BeTrue();

        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));
        sagaExists.HasValue.Should().BeTrue("Saga should be created");

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.WaitingForPayment);

        instance.Should().NotBeNull();
        instance.UserId.Should().Be(userId);
        instance.DurationDays.Should().Be(30);
        instance.Amount.Should().Be(9.99m);
        instance.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task WhenPaymentCompletedThenExtended_ShouldTransitionToExtensionCompleted()
    {
        // Arrange - Start saga
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Arrange - Payment completed
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<PaymentCompletedEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingExtension);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingExtension state");
        awaitingInstance.PaymentTransactionId.Should().Be(paymentTransactionId);

        // Act - Extension completed
        var extendedEvent = new SubscriptionExtendedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 30,
            NewExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(30).UtcDateTime,
            ExtendedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(extendedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<SubscriptionExtendedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenExtensionFailed_WithCompensation_ShouldTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new SubscriptionExtensionFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<SubscriptionExtensionFailedEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);

        instance.Should().NotBeNull();
        instance.CompensationTriggered.Should().BeTrue();
        instance.ErrorCode.Should().Be("EXTENSION_ERROR");
    }

    [Fact]
    public async Task WhenExtensionFailed_WithoutCompensation_ShouldTransitionToExtensionFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new SubscriptionExtensionFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "VALIDATION_ERROR",
            ErrorMessage = "Invalid duration",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<SubscriptionExtensionFailedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenCompensationCompleted_ShouldTransitionToCompensationCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation
        var failedEvent = new SubscriptionExtensionFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<SubscriptionExtensionFailedEvent>();

        var inProgressInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);
        inProgressInstance.Should().NotBeNull("Saga should be in CompensationInProgress state");

        // Act
        var compensationEvent = new ExtensionCompensationCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            RefundTransactionId = Guid.NewGuid(),
            CompensatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(compensationEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<ExtensionCompensationCompletedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized");
    }

    [Fact]
    public async Task WhenExtensionFailed_WithCompensation_ShouldPublishRequestRefundCommand()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var failedEvent = new SubscriptionExtensionFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<SubscriptionExtensionFailedEvent>();

        // Assert
        (await _harness.Published.Any<Finance.Payments.RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when extension fails with compensation");

        var publishedCommands = await _harness.Published.SelectAsync<Finance.Payments.RequestRefundCommand>().ToListAsync();
        var publishedCommand = publishedCommands.FirstOrDefault();
        publishedCommand.Should().NotBeNull();
        publishedCommand!.Context.Message.CorrelationId.Should().Be(correlationId);
        publishedCommand.Context.Message.UserId.Should().Be(userId);
        publishedCommand.Context.Message.PaymentTransactionId.Should().Be(paymentTransactionId);
    }

    [Fact]
    public async Task WhenExtensionInitiated_ShouldInitializeAllStateProperties()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var initiatedAt = _fakeTimeProvider.GetUtcNow().UtcDateTime;

        var initiatedEvent = new SubscriptionExtensionInitiatedEvent
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
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Consumed.Any<SubscriptionExtensionInitiatedEvent>();
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Assert
        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.WaitingForPayment);

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

    [Fact]
    public async Task WhenExtensionTimeout_ShouldTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Act
        var timeoutEvent = new ExtensionTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<ExtensionTimeoutExpired>();

        // Assert - saga should trigger compensation after extension timeout
        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.CompensationInProgress);

        instance.Should().NotBeNull("Saga should be in CompensationInProgress state after extension timeout");
        instance.CompensationTriggered.Should().BeTrue();
        instance.ErrorCode.Should().Be("EXTENSION_TIMEOUT");
    }

    [Fact]
    public async Task WhenCompensationTimeout_ShouldTransitionToCompensationFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await PublishAndWaitForPaymentCompleted(correlationId, userId, paymentMethodId, paymentTransactionId);

        // Fail with compensation
        var failedEvent = new SubscriptionExtensionFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ERROR",
            ErrorMessage = "Failed to extend",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await _harness.Bus.Publish(failedEvent);
        await _sagaHarness.Consumed.Any<SubscriptionExtensionFailedEvent>();

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

        await _harness.Bus.Publish(timeoutEvent);
        await _sagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert
        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after compensation timeout");
    }

    [Fact]
    public async Task WhenPaymentFailed_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Act
        var paymentFailedEvent = new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "INSUFFICIENT_FUNDS",
            ErrorMessage = "Insufficient funds",
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(paymentFailedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentFailedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after payment failed");
    }

    [Fact]
    public async Task WhenPaymentTimeout_ShouldTransitionToPaymentFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Act
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentTimeoutExpired>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after payment timeout");
    }

    [Fact]
    public async Task WhenDuplicateExtensionInitiatedEvent_ShouldNotCreateNewSaga()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId, paymentMethodId);

        // Act
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        await _harness.Bus.Publish(initiatedEvent);
        await Task.Delay(500);

        // Assert
        var sagas = await _sagaHarness.Sagas.SelectAsync(x => x.CorrelationId == correlationId).ToListAsync();
        sagas.Should().ContainSingle("Duplicate events should not create additional saga instances");
    }

    // -- Helper Methods --

    private SubscriptionExtensionInitiatedEvent CreateExtensionInitiatedEvent(
        Guid correlationId,
        Guid userId,
        Guid paymentMethodId,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new SubscriptionExtensionInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            DurationDays = durationDays,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"extension-{userId}-{Guid.NewGuid()}",
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
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Complete payment
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 9.99m,
            Currency = "USD",
            CompletedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(paymentCompletedEvent);
        await _sagaHarness.Consumed.Any<PaymentCompletedEvent>();

        // Verify saga is now in AwaitingExtension state
        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingExtension);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingExtension state");
    }
}