using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Payments.Transactions;
using Platform.Test.Framework.Kafka;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.UnitTests.Sagas;

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
public class PaymentProcessingSagaOrchestratorTests : IAsyncLifetime
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly FakeOutboxWriter _fakeOutboxWriter = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _testHarness = null!;

    private ISagaStateMachineTestHarness<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState> _sagaHarness =
        null!;

    public async ValueTask InitializeAsync()
    {
        var sagaOptions = SagaTestFixture.CreateSagaOptions();
        var topicsOptions = SagaTestFixture.CreateSagaTopicsOptions();
        var testDbName = $"SagaTest_{Guid.CreateVersion7()}";

        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<PaymentProcessingSagaOrchestrator>>())
            .AddSingleton(sagaOptions)
            .AddSingleton(topicsOptions)
            .AddSingleton<TimeProvider>(_fakeTimeProvider)
            .AddSagaOutboxTestServices(testDbName, _fakeOutboxWriter)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _testHarness = _provider.GetRequiredService<ITestHarness>();
        _sagaHarness = _testHarness
            .GetSagaStateMachineHarness<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState>();
        await _testHarness.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _testHarness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldTransitionToAwaitingAuthorization()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentMethodId = $"pm_{Guid.CreateVersion7():N}",
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"payment-{userId}-{Guid.CreateVersion7()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);

        // Assert
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var awaitingAuthorizationSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            awaitingAuthorizationSagaState.Should().NotBeNull();
            awaitingAuthorizationSagaState.UserId.Should().Be(userId);
            awaitingAuthorizationSagaState.Amount.Should().Be(9.99m);
            awaitingAuthorizationSagaState.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task WhenPaymentAuthorized_ShouldTransitionToAwaitingCapture()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            AuthorizedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentAuthorizedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentAuthorizedSagaEvent>()).Should().BeTrue();

        var awaitingCaptureSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCapture);

        using (new AssertionScope())
        {
            awaitingCaptureSagaState.Should().NotBeNull("Saga should be in AwaitingCapture state");
            awaitingCaptureSagaState.AuthorizationId.Should().Be(authorizationId);
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldTransitionToPaymentCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCapturedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCapturedSagaEvent>()).Should().BeTrue();

        var paymentCompletedSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.PaymentCompleted);

        using (new AssertionScope())
        {
            paymentCompletedSagaState.Should().NotBeNull("Saga should be in PaymentCompleted state");
            paymentCompletedSagaState.PaymentTransactionId.Should().Be(paymentTransactionId);
            paymentCompletedSagaState.CapturedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldPublishPaymentCompletedEventToKafka()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCapturedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentCapturedSagaEvent>();

        // Assert - verify message was added to the transactional outbox
        var outboxMessages = _fakeOutboxWriter.GetMessages<PaymentCompletedEvent>().ToList();

        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<PaymentCompletedEvent>().Should().BeTrue(
                "PaymentCompletedEvent should be added to the outbox for publishing to Kafka");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.PaymentTransactionId.Should().Be(paymentTransactionId);
        }
    }

    [Fact]
    public async Task WhenAuthorizationFailed_NonRetryable_ShouldTransitionToAuthorizationFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var paymentAuthorizationFailedSagaEvent = new PaymentAuthorizationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card declined",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentAuthorizationFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentAuthorizationFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after non-retryable auth failure");
    }

    [Fact]
    public async Task WhenCaptureFailed_NonRetryable_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Unable to capture funds",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCaptureFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCaptureFailedSagaEvent>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull("Saga should transition to VoidInProgress after capture failure");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenPaymentVoided_ShouldTransitionToVoidCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Fail capture to get to VoidInProgress
        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Unable to capture funds",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCaptureFailedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentCaptureFailedSagaEvent>();

        // Act
        var paymentVoidedSagaEvent = new PaymentVoidedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            VoidedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentVoidedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentVoidedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after void completed");
    }

    [Fact]
    public async Task WhenRefundRequested_FromPaymentCompleted_ShouldTransitionToRefundInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForCapture(correlationId, userId, authorizationId, paymentTransactionId);

        // Act - request refund
        var paymentRefundRequestedSagaEvent = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentRefundRequestedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentRefundRequestedSagaEvent>()).Should().BeTrue();

        var refundInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.RefundInProgress);

        using (new AssertionScope())
        {
            refundInProgressSagaState.Should().NotBeNull("Saga should be in RefundInProgress state");
            refundInProgressSagaState.CompensationTriggered.Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenRefundCompleted_ShouldTransitionToRefundCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForCapture(correlationId, userId, authorizationId, paymentTransactionId);

        // Request refund
        var paymentRefundRequestedSagaEvent = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentRefundRequestedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentRefundRequestedSagaEvent>();

        // Act - refund completed
        var paymentRefundCompletedSagaEvent = new PaymentRefundCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentRefundCompletedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentRefundCompletedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after refund completed");
    }

    [Fact]
    public async Task WhenAuthorizationTimeout_ShouldTransitionToAuthorizationFailed()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var authorizationTimeoutExpired = new AuthorizationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(authorizationTimeoutExpired);

        // Assert
        (await _sagaHarness.Consumed.Any<AuthorizationTimeoutExpired>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(correlationId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after authorization timeout");
    }

    [Fact]
    public async Task WhenCaptureTimeout_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        // Act
        var captureTimeoutExpired = new CaptureTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await _testHarness.Bus.Publish(captureTimeoutExpired);

        // Assert
        (await _sagaHarness.Consumed.Any<CaptureTimeoutExpired>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull("Saga should be in VoidInProgress after capture timeout");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldPublishRequestPaymentAuthorizationCommand()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = $"pm_{Guid.CreateVersion7():N}";
        var orderId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            UserId = userId,
            PaymentMethodId = paymentMethodId,
            Amount = 9.99m,
            Currency = "USD",
            IdempotencyKey = $"payment-{userId}-test",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        // Act
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>();

        // Assert - verify message was added to the transactional outbox
        var outboxMessages = _fakeOutboxWriter.GetMessages<AuthorizePaymentCommand>().ToList();

        using (new AssertionScope())
        {
            _fakeOutboxWriter.HasMessage<AuthorizePaymentCommand>().Should().BeTrue(
                "AuthorizePaymentCommand should be added to the outbox");
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
            outboxMessages[0].IntegrationEvent.OrderId.Should().Be(orderId);
            outboxMessages[0].IntegrationEvent.UserId.Should().Be(userId);
            outboxMessages[0].IntegrationEvent.PaymentMethodId.Should().Be(paymentMethodId);
        }
    }

    [Fact]
    public async Task WhenPaymentInitiated_SagaStateCarriesFreshPaymentTransactionId()
    {
        // Cross-cutting wave1-followup #255:
        // The saga must mint a fresh UUID v7 PaymentTransactionId at initial state — this becomes
        // the Payments aggregate's primary key on the outbound AuthorizePaymentCommand. The id is
        // distinct from CorrelationId; the v1 collapse where they coincided is being unwound.
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);

        // Act
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Assert
        var awaitingAuthorizationSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            awaitingAuthorizationSagaState.Should().NotBeNull();
            awaitingAuthorizationSagaState.PaymentTransactionId.Should().NotBeNull(
                "the saga issues PaymentTransactionId up front per wave1-followup #255");
            awaitingAuthorizationSagaState.PaymentTransactionId!.Value.Should().NotBeEmpty();
            awaitingAuthorizationSagaState.PaymentTransactionId.Value.Should().NotBe(correlationId,
                "PaymentTransactionId must be distinct from CorrelationId — no v1 collapse");
            IsUuidV7(awaitingAuthorizationSagaState.PaymentTransactionId.Value).Should().BeTrue(
                "Guid.CreateVersion7() is required per ADR-0008 ID-format guidance");
        }
    }

    [Fact]
    public async Task WhenPaymentInitiated_PublishedAuthorizeCommand_CarriesPaymentTransactionId()
    {
        // Cross-cutting wave1-followup #255:
        // The Avro AuthorizePaymentCommand must carry the saga-issued PaymentTransactionId so the
        // Payments-side mapper can use it as the aggregate PK (AppAuthorizePaymentCommand.PaymentId).
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);

        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>();

        var outboxMessage = _fakeOutboxWriter.GetMessages<AuthorizePaymentCommand>().Single();
        var sagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            outboxMessage.IntegrationEvent.PaymentTransactionId.Should().Be(
                sagaState.PaymentTransactionId!.Value,
                "Avro PaymentTransactionId matches the saga-state value the Payments aggregate will adopt as its PK");
            outboxMessage.IntegrationEvent.PaymentTransactionId.Should().NotBe(correlationId,
                "PaymentTransactionId must be distinct from CorrelationId on the wire");
        }
    }

    [Fact]
    public async Task WhenAuthorizationFailedRetryable_RetriedAuthorizeCommand_ReusesPaymentTransactionId()
    {
        // Cross-cutting wave1-followup #255:
        // The saga must stick with the originally-minted PaymentTransactionId on retry so the
        // Payments aggregate can identify the existing row (one-payment-per-saga, idempotent retry).
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await _testHarness.Bus.Publish(CreatePaymentInitiatedEvent(correlationId, userId));
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var initialState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);
        var originalPaymentTransactionId = initialState.PaymentTransactionId;

        // Act: retryable auth failure → saga must republish AuthorizePaymentCommand
        await _testHarness.Bus.Publish(new PaymentAuthorizationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "GATEWAY_TIMEOUT",
            ErrorMessage = "transient",
            IsRetryable = true,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        });
        await _sagaHarness.Consumed.Any<PaymentAuthorizationFailedSagaEvent>();

        // Assert
        var authorizeMessages = _fakeOutboxWriter.GetMessages<AuthorizePaymentCommand>().ToList();

        using (new AssertionScope())
        {
            authorizeMessages.Should().HaveCount(2, "the initial publish + one retry");
            authorizeMessages[1].IntegrationEvent.PaymentTransactionId.Should().Be(
                originalPaymentTransactionId!.Value,
                "the retried command must reuse the original PaymentTransactionId so Payments idempotently re-authorizes the same aggregate row");
        }
    }

    private static bool IsUuidV7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] >> 4) == 0x7;
    }

    private PaymentInitiatedSagaEvent CreatePaymentInitiatedEvent(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new PaymentInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            OrderId = Guid.CreateVersion7(),
            UserId = userId,
            PaymentMethodId = $"pm_{Guid.CreateVersion7():N}",
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.CreateVersion7()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };
    }

    private async Task PublishAndWaitForAuthorization(
        Guid correlationId,
        Guid userId,
        string authorizationId)
    {
        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(correlationId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            AuthorizedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentAuthorizedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentAuthorizedSagaEvent>();

        var awaitingCaptureSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCapture);
        awaitingCaptureSagaState.Should().NotBeNull("Saga should be in AwaitingCapture state");
    }

    private async Task PublishAndWaitForCapture(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        Guid paymentTransactionId)
    {
        await PublishAndWaitForAuthorization(correlationId, userId, authorizationId);

        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCapturedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentCapturedSagaEvent>();

        var paymentCompletedSagaState = _sagaHarness.Sagas.ContainsInState(
            correlationId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.PaymentCompleted);
        paymentCompletedSagaState.Should().NotBeNull("Saga should be in PaymentCompleted state");
    }
}
