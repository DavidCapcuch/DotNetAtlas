using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Commands;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace DotNetAtlas.Sagas.UnitTests.Sagas;

/// <summary>
/// Unit tests for the PaymentProcessingSaga state machine.
/// Tests verify correct state transitions, event handling, timeout scenarios, and compensation logic.
/// </summary>
/// <remarks>
/// The saga flow is:
/// 1. PaymentInitiatedEvent → AwaitingAuthorization (publishes RequestPaymentAuthorizationCommand)
/// 2. PaymentAuthorizedEvent → AwaitingCapture (publishes RequestPaymentCaptureCommand)
/// 3. PaymentCapturedEvent → PaymentCompleted (publishes PaymentCompletedEvent to Kafka)
/// The saga remains alive in PaymentCompleted to handle potential refund requests.
/// </remarks>
public class PaymentProcessingSagaTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<PaymentProcessingSaga, PaymentSagaState> _sagaHarness = null!;

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
            .AddSingleton(Substitute.For<ILogger<PaymentProcessingSaga>>())
            .AddSingleton(sagaOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<PaymentProcessingSaga, PaymentSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _harness.GetSagaStateMachineHarness<PaymentProcessingSaga, PaymentSagaState>();
        await _harness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldTransitionToAwaitingAuthorization()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = new PaymentInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = Guid.NewGuid(),
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"payment-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _harness.Bus.Publish(initiatedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentInitiatedEvent>()).Should().BeTrue();

        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));
        sagaExists.HasValue.Should().BeTrue("Saga should be created");

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingAuthorization);

        instance.Should().NotBeNull();
        instance.UserId.Should().Be(userId);
        instance.Amount.Should().Be(9.99m);
        instance.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task WhenPaymentAuthorized_ShouldTransitionToAwaitingCapture()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Act
        var authorizedEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            AuthorizedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await _harness.Bus.Publish(authorizedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentAuthorizedEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingCapture);

        instance.Should().NotBeNull("Saga should be in AwaitingCapture state");
        instance.AuthorizationId.Should().Be(authorizationId);
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldTransitionToPaymentCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(capturedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCapturedEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.PaymentCompleted);

        instance.Should().NotBeNull("Saga should be in PaymentCompleted state");
        instance.PaymentTransactionId.Should().Be(paymentTransactionId);
        instance.CapturedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldPublishPaymentCompletedEventToKafka()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(capturedEvent);
        await _sagaHarness.Consumed.Any<PaymentCapturedEvent>();

        // Assert
        (await _harness.Published.Any<Finance.Payments.PaymentCompletedEvent>()).Should().BeTrue(
            "PaymentCompletedEvent should be published to Kafka");

        var publishedEvents = await _harness.Published.SelectAsync<Finance.Payments.PaymentCompletedEvent>().ToListAsync();
        var publishedEvent = publishedEvents.FirstOrDefault();
        publishedEvent.Should().NotBeNull();
        publishedEvent!.Context.Message.CorrelationId.Should().Be(correlationId);
        publishedEvent.Context.Message.PaymentTransactionId.Should().Be(paymentTransactionId);
    }

    [Fact]
    public async Task WhenAuthorizationFailed_NonRetryable_ShouldTransitionToAuthorizationFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Act
        var failedEvent = new PaymentAuthorizationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card declined",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentAuthorizationFailedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after non-retryable auth failure");
    }

    [Fact]
    public async Task WhenCaptureFailed_NonRetryable_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var failedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Unable to capture funds",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(failedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCaptureFailedEvent>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.VoidInProgress);

        instance.Should().NotBeNull("Saga should transition to VoidInProgress after capture failure");
        instance.CompensationTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentVoided_ShouldTransitionToVoidCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Fail capture to get to VoidInProgress
        var captureFailed = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Unable to capture funds",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(captureFailed);
        await _sagaHarness.Consumed.Any<PaymentCaptureFailedEvent>();

        // Act
        var voidedEvent = new PaymentVoidedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            VoidedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(voidedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentVoidedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after void completed");
    }

    [Fact]
    public async Task WhenRefundRequested_FromPaymentCompleted_ShouldTransitionToRefundInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForCapture(correlationId, userId, authorizationId, paymentTransactionId);

        // Act - request refund
        var refundCommand = new RequestPaymentRefundCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(refundCommand);

        // Assert
        (await _sagaHarness.Consumed.Any<RequestPaymentRefundCommand>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.RefundInProgress);

        instance.Should().NotBeNull("Saga should be in RefundInProgress state");
        instance.CompensationTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task WhenRefundCompleted_ShouldTransitionToRefundCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForCapture(correlationId, userId, authorizationId, paymentTransactionId);

        // Request refund
        var refundCommand = new RequestPaymentRefundCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(refundCommand);
        await _sagaHarness.Consumed.Any<RequestPaymentRefundCommand>();

        // Act - refund completed
        var refundCompletedEvent = new PaymentRefundCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.NewGuid(),
            RefundedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(refundCompletedEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentRefundCompletedEvent>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after refund completed");
    }

    [Fact]
    public async Task WhenAuthorizationTimeout_ShouldTransitionToAuthorizationFailed()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        // Act
        var timeoutEvent = new AuthorizationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<AuthorizationTimeoutExpired>()).Should().BeTrue();

        var finalState = await _sagaHarness.NotExists(correlationId, timeout: TimeSpan.FromSeconds(5));
        finalState.HasValue.Should().BeFalse("Saga should be finalized after authorization timeout");
    }

    [Fact]
    public async Task WhenCaptureTimeout_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var timeoutEvent = new CaptureTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _harness.Bus.Publish(timeoutEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<CaptureTimeoutExpired>()).Should().BeTrue();

        var instance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.VoidInProgress);

        instance.Should().NotBeNull("Saga should be in VoidInProgress after capture timeout");
        instance.CompensationTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldPublishRequestPaymentAuthorizationCommand()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();

        var initiatedEvent = new PaymentInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"payment-{userId}-test",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Assert
        (await _harness.Published.Any<RequestPaymentAuthorizationCommand>()).Should().BeTrue(
            "RequestPaymentAuthorizationCommand should be published");

        var publishedCommands = await _harness.Published.SelectAsync<RequestPaymentAuthorizationCommand>().ToListAsync();
        var publishedCommand = publishedCommands.FirstOrDefault();
        publishedCommand.Should().NotBeNull();
        publishedCommand!.Context.Message.CorrelationId.Should().Be(correlationId);
        publishedCommand.Context.Message.UserId.Should().Be(userId);
        publishedCommand.Context.Message.PaymentMethodId.Should().Be(paymentMethodId);
    }

    // -- Helper Methods --

    private PaymentInitiatedEvent CreatePaymentInitiatedEvent(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new PaymentInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = Guid.NewGuid(),
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };
    }

    private async Task PublishAndWaitForAuthorization(
        Guid correlationId,
        Guid userId,
        string authorizationId)
    {
        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _harness.Bus.Publish(initiatedEvent);
        await _sagaHarness.Exists(correlationId, timeout: TimeSpan.FromSeconds(5));

        var authorizedEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            AuthorizedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await _harness.Bus.Publish(authorizedEvent);
        await _sagaHarness.Consumed.Any<PaymentAuthorizedEvent>();

        var awaitingInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.AwaitingCapture);
        awaitingInstance.Should().NotBeNull("Saga should be in AwaitingCapture state");
    }

    private async Task PublishAndWaitForCapture(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        Guid paymentTransactionId)
    {
        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _harness.Bus.Publish(capturedEvent);
        await _sagaHarness.Consumed.Any<PaymentCapturedEvent>();

        var completedInstance = _sagaHarness.Sagas.ContainsInState(
            correlationId,
            _sagaHarness.StateMachine,
            _sagaHarness.StateMachine.PaymentCompleted);
        completedInstance.Should().NotBeNull("Saga should be in PaymentCompleted state");
    }
}