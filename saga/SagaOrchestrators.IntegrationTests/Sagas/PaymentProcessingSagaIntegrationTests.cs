using Microsoft.EntityFrameworkCore;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.Test.Framework.Assertions;
using SagaOrchestrators.IntegrationTests.Common;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.IntegrationTests.Sagas;

[Collection(nameof(SagaTestCollection))]
public class PaymentProcessingSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private SagaStateMonitor<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState> SagaStateMonitor { get; }

    public PaymentProcessingSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<PaymentProcessingSagaOrchestrator, PaymentProcessingSagaState>();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldTransitionToAndPersistAwaitingAuthorization()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentRequestedEvent = CreateRequestPaymentCommand(correlationId, userId);

        // Act
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            paymentRequestedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);
        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingAuthorization));
            persistedState.Amount.Should().Be(9.99m);
            persistedState.Currency.Should().Be("USD");
            outboxMessages.Should().ContainSingle();
            outboxMessages.Should().ContainSingleMessageOfType<AuthorizePaymentCommand>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldTransitionToAndPersistPaymentCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Wave1-followup #255: saga mints PaymentTransactionId in Initial state and rejects (throws)
        // any inbound PaymentCapturedEvent whose PaymentTransactionId does not match. Tests must
        // therefore echo back the saga's minted value rather than fabricate a new Guid.
        var sagaMintedPaymentTransactionId = await ReadSagaMintedPaymentTransactionIdAsync(correlationId);

        // Act - Capture the payment
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = sagaMintedPaymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 99.99m.ToAvroDecimal(4),
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, capturedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.PaymentCompleted));
            persistedState.PaymentTransactionId.Should().Be(sagaMintedPaymentTransactionId);
            persistedState.AuthorizationId.Should().Be(authorizationId);
            persistedState.CapturedAtUtc.Should().NotBeNull();

            outboxMessages.Should().ContainMessageOfType<PaymentCompletedEvent>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenMultipleSagasInitiated_ShouldMaintainIsolatedStates()
    {
        // Arrange
        var correlationId1 = Guid.CreateVersion7();
        var correlationId2 = Guid.CreateVersion7();
        var userId1 = Guid.CreateVersion7();
        var userId2 = Guid.CreateVersion7();

        var event1 = CreateRequestPaymentCommand(correlationId1, userId1, 9.99m, "USD");
        var event2 = CreateRequestPaymentCommand(correlationId2, userId2, 99.99m, "EUR");

        // Act
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId1, event1);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId2, event2);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId1, state => state.AwaitingAuthorization, DefaultTimeout);
        await SagaStateMonitor.WaitForStateAsync(correlationId2, state => state.AwaitingAuthorization, DefaultTimeout);

        var state1 = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await SagaDbContext.PaymentProcessingSagaStates
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
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = $"pm_{Guid.CreateVersion7():N}";
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        // Step 1: Initiate payment
        var paymentRequestedEvent = CreateRequestPaymentCommand(correlationId, userId, 49.99m, "USD", paymentMethodId);

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            paymentRequestedEvent);

        // Verify: AwaitingAuthorization state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingAuthorization, DefaultTimeout);
        var stateAfterInitiation = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterInitiation.Should().NotBeNull();
            stateAfterInitiation.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingAuthorization));
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

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, authorizedEvent);

        // Verify: AwaitingCapture state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCapture, DefaultTimeout);
        var stateAfterAuthorization = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterAuthorization.Should().NotBeNull();
            stateAfterAuthorization.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture));
            stateAfterAuthorization.AuthorizationId.Should().Be(authorizationId);
            stateAfterAuthorization.AuthorizedAtUtc.Should().HaveValue();
        }

        // Step 3: Capture payment
        // Wave1-followup #255: echo saga's minted PaymentTransactionId on the capture event.
        var sagaMintedPaymentTransactionId = stateAfterAuthorization!.PaymentTransactionId!.Value;
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = sagaMintedPaymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 49.99m.ToAvroDecimal(4),
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, capturedEvent);

        // Verify: PaymentCompleted state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
        var stateAfterCapture = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            stateAfterCapture.Should().NotBeNull();
            stateAfterCapture.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.PaymentCompleted));
            stateAfterCapture.PaymentTransactionId.Should().Be(sagaMintedPaymentTransactionId);
            stateAfterCapture.CapturedAtUtc.Should().HaveValue();
            stateAfterCapture.CompensationTriggered.Should().BeFalse();

            outboxMessages.Should().ContainMessageOfType<AuthorizePaymentCommand>(correlationId.ToString());
            outboxMessages.Should().ContainMessageOfType<CapturePaymentCommand>(correlationId.ToString());
            outboxMessages.Should().ContainMessageOfType<PaymentCompletedEvent>(correlationId.ToString());
        }
    }

    private RequestPaymentCommand CreateRequestPaymentCommand(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD",
        string? paymentMethodId = null,
        Guid? orderId = null)
    {
        return new RequestPaymentCommand
        {
            CorrelationId = correlationId,
            OrderId = orderId ?? Guid.CreateVersion7(),
            UserId = userId,
            // C-2 closeout: Payments wire shape is string. Default to a Stripe-style token.
            PaymentMethodId = paymentMethodId ?? $"pm_{Guid.CreateVersion7():N}",
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.CreateVersion7()}",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };
    }

    [Fact]
    public async Task WhenAuthorizationFailsNonRetryable_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentRequestedEvent = CreateRequestPaymentCommand(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
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

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, authFailedEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCaptureFailsNonRetryable_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Act - Send non-retryable capture failure
        var captureFailedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            // Upstream-owned code emitted by the Payments BC's gateway adapter on PaymentCaptureFailedEvent.ErrorCode;
            // not extracted to PaymentProcessingSagaErrorCodes because saga is a consumer of this vocabulary, not the owner.
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed permanently",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId,
            captureFailedEvent);
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);

        // Assert
        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.VoidInProgress));
            persistedState.CompensationTriggered.Should().BeTrue();

            outboxMessages.Should().ContainMessageOfType<VoidPaymentCommand>(correlationId.ToString());
            outboxMessages.Should().ContainMessageOfType<PaymentFailedEvent>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenVoidCompletes_ShouldFinalizeInVoidCompletedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

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

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, voidedEvent);

        // Assert - verify saga finalized (removed from database)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenRefundRequested_ShouldTransitionToRefundInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Transition to PaymentCompleted state; helper returns the saga-minted PaymentTransactionId
        // that must be echoed on the downstream PaymentRefundRequestedSagaEvent (wave1-followup #255).
        var paymentTransactionId = await TransitionSagaToPaymentCompletedState(correlationId, userId);

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
        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.RefundInProgress));

            outboxMessages.Should().ContainMessageOfType<RequestRefundCommand>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenAuthorizationTimesOut_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentRequestedEvent = CreateRequestPaymentCommand(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
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
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId);

        // Act - Simulate timeout by publishing CaptureTimeoutExpired (MassTransit internal)
        var timeoutEvent = new CaptureTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);
        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.VoidInProgress));
            persistedState.CompensationTriggered.Should().BeTrue();

            outboxMessages.Should().ContainMessageOfType<VoidPaymentCommand>(correlationId.ToString());
            outboxMessages.Should().ContainMessageOfType<PaymentFailedEvent>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenVoidTimesOut_ShouldFinalizeInVoidFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

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
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await TransitionSagaToRefundInProgressState(correlationId, userId);

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
        var paymentRequestedEvent = CreateRequestPaymentCommand(correlationId, userId, amount, currency);
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
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

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, authorizedEvent);

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
            // Upstream-owned code emitted by the Payments BC's gateway adapter on PaymentCaptureFailedEvent.ErrorCode;
            // not extracted to PaymentProcessingSagaErrorCodes because saga is a consumer of this vocabulary, not the owner.
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId,
            captureFailedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);
    }

    /// <summary>
    /// Drives the saga to <c>PaymentCompleted</c> and returns the saga-minted PaymentTransactionId
    /// (wave1-followup #255). Callers MUST use this value on any subsequent event whose
    /// PaymentTransactionId the saga validates (refund-request, refund-completed).
    /// </summary>
    private async Task<Guid> TransitionSagaToPaymentCompletedState(
        Guid correlationId,
        Guid userId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        var sagaMintedPaymentTransactionId = await ReadSagaMintedPaymentTransactionIdAsync(correlationId);
        var capturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = sagaMintedPaymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsTransactions, userId, capturedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.PaymentCompleted, DefaultTimeout);
        return sagaMintedPaymentTransactionId;
    }

    private async Task TransitionSagaToRefundInProgressState(
        Guid correlationId,
        Guid userId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        var paymentTransactionId = await TransitionSagaToPaymentCompletedState(correlationId, userId, amount, currency);

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

    /// <summary>
    /// Reads the saga's PaymentTransactionId from the persisted state. Must be called after the
    /// saga has reached at least <c>AwaitingAuthorization</c> (the Initial transition mints it
    /// per wave1-followup #255). Used by tests that need to echo the value back on a downstream
    /// event the saga's mismatch-guard would otherwise throw on.
    /// </summary>
    private async Task<Guid> ReadSagaMintedPaymentTransactionIdAsync(Guid correlationId)
    {
        var state = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);
        return state?.PaymentTransactionId
            ?? throw new InvalidOperationException(
                $"Saga {correlationId} not found or PaymentTransactionId not minted — "
                + "wave1-followup #255 invariant violation");
    }
}
