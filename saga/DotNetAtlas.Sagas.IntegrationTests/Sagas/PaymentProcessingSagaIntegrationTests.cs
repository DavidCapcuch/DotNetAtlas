using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using Finance.Payments;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the PaymentProcessingSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// Events are produced to real Kafka topics and flow through the full consumer pipeline.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class PaymentProcessingSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private SagaStateMonitor<PaymentProcessingSaga, PaymentProcessingSagaState> SagaStateMonitor { get; }

    public PaymentProcessingSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<PaymentProcessingSaga, PaymentProcessingSagaState>();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldTransitionToAndPersistAwaitingAuthorization()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var paymentRequestedEvent = CreatePaymentRequestedEvent(correlationId, userId);

        // Act
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            paymentRequestedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be("AwaitingAuthorization");
            persistedState.Amount.Should().Be(9.99m);
            persistedState.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldTransitionToAndPersistPaymentCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Act - Capture the payment
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 99.99m.ToAvroDecimal(4),
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, capturedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("PaymentCompleted");
            persistedState.PaymentTransactionId.Should().Be(paymentTransactionId);
            persistedState.AuthorizationId.Should().Be(authorizationId);
            persistedState.CapturedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task WhenMultipleSagasInitiated_ShouldMaintainIsolatedStates()
    {
        // Arrange
        var correlationId1 = Guid.NewGuid();
        var correlationId2 = Guid.NewGuid();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        var event1 = CreatePaymentRequestedEvent(correlationId1, userId1, 9.99m, "USD");
        var event2 = CreatePaymentRequestedEvent(correlationId2, userId2, 99.99m, "EUR");

        // Act
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId1, event1);
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId2, event2);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId1, state => state.AwaitingAuthorization, DefaultTimeout);
        await SagaStateMonitor.WaitForStateAsync(correlationId2, state => state.AwaitingAuthorization, DefaultTimeout);

        var state1 = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        using (new AssertionScope())
        {
            state1.Should().NotBeNull();
            state2.Should().NotBeNull();
            state1.Amount.Should().Be(9.99m);
            state1.Currency.Should().Be("USD");
            state2.Amount.Should().Be(99.99m);
            state2.Currency.Should().Be("EUR");
        }
    }

    [Fact]
    public async Task WhenFullPaymentFlow_ShouldPersistStateAtEachTransition()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        // Step 1: Initiate payment
        var paymentRequestedEvent = CreatePaymentRequestedEvent(correlationId, userId, 49.99m, "USD", paymentMethodId);

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            paymentRequestedEvent);

        // Verify: AwaitingAuthorization state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);
        var stateAfterInitiation = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterInitiation.Should().NotBeNull();
            stateAfterInitiation.CurrentState.Should().Be("AwaitingAuthorization");
            stateAfterInitiation.UserId.Should().Be(userId);
            stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
            stateAfterInitiation.Amount.Should().Be(49.99m);
            stateAfterInitiation.Currency.Should().Be("USD");
            stateAfterInitiation.InitiatedAtUtc.Should().NotBe(default);
        }

        // Step 2: Authorize payment
        var authorizedEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 49.99m.ToAvroDecimal(4),
            Currency = "USD",
            AuthorizedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, authorizedEvent);

        // Verify: AwaitingCapture state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCapture, DefaultTimeout);
        var stateAfterAuthorization = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterAuthorization.Should().NotBeNull();
            stateAfterAuthorization.CurrentState.Should().Be("AwaitingCapture");
            stateAfterAuthorization.AuthorizationId.Should().Be(authorizationId);
            stateAfterAuthorization.AuthorizedAtUtc.Should().HaveValue();
        }

        // Step 3: Capture payment
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 49.99m.ToAvroDecimal(4),
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, capturedEvent);

        // Verify: PaymentCompleted state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
        var stateAfterCapture = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterCapture.Should().NotBeNull();
            stateAfterCapture.CurrentState.Should().Be("PaymentCompleted");
            stateAfterCapture.PaymentTransactionId.Should().Be(paymentTransactionId);
            stateAfterCapture.CapturedAtUtc.Should().HaveValue();
            stateAfterCapture.CompensationTriggered.Should().BeFalse();

            FakeOutboxWriter.HasMessage<PaymentCompletedEvent>().Should().BeTrue(
                "PaymentCompletedEvent should be added to the outbox for publishing to Kafka when payment is captured");
            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentCompletedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    private PaymentRequestedEvent CreatePaymentRequestedEvent(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new PaymentRequestedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.NewGuid(),
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.NewGuid()}",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };
    }

    [Fact]
    public async Task WhenAuthorizationFailsNonRetryable_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var paymentRequestedEvent = CreatePaymentRequestedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            paymentRequestedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);

        // Act - Send non-retryable authorization failure
        var authFailedEvent = new PaymentAuthorizationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card was declined by issuer",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, authFailedEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCaptureFailsNonRetryable_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Act - Send non-retryable capture failure
        var captureFailedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed permanently",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            captureFailedEvent);
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);

        // Assert
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("VoidInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeTrue(
                "PaymentFailedEvent should be added to the outbox when capture fails non-retryable");
            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentFailedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    [Fact]
    public async Task WhenVoidCompletes_ShouldFinalizeInVoidCompletedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        // Transition to VoidInProgress via capture failure
        await TransitionSagaToVoidInProgressState(correlationId, userId, authorizationId);

        // Act - Complete the void
        var voidedEvent = new PaymentVoidedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            VoidedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, voidedEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenRefundRequested_ShouldTransitionToRefundInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        // Transition to PaymentCompleted state
        await TransitionSagaToPaymentCompletedState(correlationId, userId, paymentTransactionId);

        // Act - Request refund (internal saga event - no Kafka consumer exists)
        var refundCommand = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await Bus.Publish(refundCommand);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.RefundInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("RefundInProgress");
        }
    }

    [Fact]
    public async Task WhenAuthorizationTimesOut_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var paymentRequestedEvent = CreatePaymentRequestedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            paymentRequestedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);

        // Act - Simulate timeout by publishing AuthorizationTimeoutExpired (MassTransit internal)
        var timeoutEvent = new AuthorizationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCaptureTimesOut_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Act - Simulate timeout by publishing CaptureTimeoutExpired (MassTransit internal)
        var timeoutEvent = new CaptureTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("VoidInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeTrue(
                "PaymentFailedEvent should be added to the outbox when capture times out");
            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentFailedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    [Fact]
    public async Task WhenVoidTimesOut_ShouldFinalizeInVoidFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await TransitionSagaToVoidInProgressState(correlationId, userId, authorizationId);

        // Act - Simulate timeout by publishing VoidTimeoutExpired (MassTransit internal)
        var timeoutEvent = new VoidTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenRefundTimesOut_ShouldFinalizeInRefundFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToRefundInProgressState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing RefundTimeoutExpired (MassTransit internal)
        var timeoutEvent = new RefundTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    // -- Helper Methods --

    private async Task TransitionSagaToAwaitingCaptureState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        var paymentRequestedEvent = CreatePaymentRequestedEvent(correlationId, userId, amount, currency);
        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            paymentRequestedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);

        var authorizedEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            AuthorizedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, authorizedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCapture, DefaultTimeout);
    }

    private async Task TransitionSagaToVoidInProgressState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        var captureFailedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId,
            captureFailedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);
    }

    private async Task TransitionSagaToPaymentCompletedState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        var authorizationId = $"auth-{Guid.NewGuid()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(SagaIntegrationTestFixture.FinancePaymentsTopic, userId, capturedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
    }

    private async Task TransitionSagaToRefundInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToPaymentCompletedState(correlationId, userId, paymentTransactionId, amount, currency);

        // RefundRequested is an internal saga event (no Kafka consumer exists)
        var refundCommand = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Test refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await Bus.Publish(refundCommand);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.RefundInProgress, DefaultTimeout);
    }
}
