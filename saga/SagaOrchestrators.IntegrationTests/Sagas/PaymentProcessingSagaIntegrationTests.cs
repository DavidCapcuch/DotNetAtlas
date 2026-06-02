using Microsoft.EntityFrameworkCore;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Platform.Test.Framework.Assertions;
using SagaOrchestrators.IntegrationTests.Common;
using SagaOrchestrators.Payments.PaymentProcessingSaga;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the PaymentProcessingSaga over real Postgres + Kafka after the ADR-0026
/// capture-pivot restructure: authorize → await the Checkout saga's capture-approval / abort
/// signal → capture → finalize, with a pre-capture void on the compensation path. The saga issues
/// commands only — the Payments service owns and publishes the terminal events, so this suite
/// asserts the saga does NOT outbox <c>PaymentCompletedEvent</c> / <c>PaymentFailedEvent</c>.
/// </summary>
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
    public async Task WhenPaymentAuthorized_ShouldTransitionToAndPersistAwaitingCaptureApproval()
    {
        // ADR-0026: capture is deferred. On authorization the saga parks in AwaitingCaptureApproval
        // and must NOT have issued a CapturePaymentCommand.
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureApproval(correlationId, userId, authorizationId);

        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingCaptureApproval));
            persistedState.AuthorizationId.Should().Be(authorizationId);
            persistedState.AuthorizedAtUtc.Should().HaveValue();
            outboxMessages.Where(om => om.Type == typeof(CapturePaymentCommand).FullName)
                .Should().BeEmpty("capture is deferred to the pivot — no CapturePaymentCommand until approval");
        }
    }

    [Fact]
    public async Task WhenCaptureApproved_ShouldTransitionToAwaitingCapture_AndIssueCapturePaymentCommand()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCapture(correlationId, userId, authorizationId);

        var persistedState = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingCapture));
            outboxMessages.Should().ContainMessageOfType<CapturePaymentCommand>(correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldFinalize_WithoutPublishingTerminalEvent()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCapture(correlationId, userId, authorizationId);

        // Wave1-followup #255: echo the saga-minted PaymentTransactionId on the capture event.
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

        // Assert - the saga reaches its successful terminal and finalizes (removed from the table)
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue("the sub-saga finalizes on a successful capture");
            // ADR-0026: Payments owns the terminal PaymentCompletedEvent — the sub-saga must not
            // outbox it.
            outboxMessages.Where(om => om.Type == typeof(PaymentCompletedEvent).FullName)
                .Should().BeEmpty("Payments owns the terminal PaymentCompletedEvent, not the sub-saga");
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

        // Step 2: Authorize payment → AwaitingCaptureApproval (no capture yet)
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

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCaptureApproval, DefaultTimeout);
        var stateAfterAuthorization = await SagaDbContext.PaymentProcessingSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterAuthorization.Should().NotBeNull();
            stateAfterAuthorization.CurrentState.Should().Be(nameof(PaymentProcessingSagaOrchestrator.AwaitingCaptureApproval));
            stateAfterAuthorization.AuthorizationId.Should().Be(authorizationId);
            stateAfterAuthorization.AuthorizedAtUtc.Should().HaveValue();
        }

        // Step 3: Checkout saga approves capture → AwaitingCapture (issues CapturePaymentCommand)
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            CreateApproveCaptureCommand(correlationId, userId));

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCapture, DefaultTimeout);

        // Step 4: Capture payment → finalize (no terminal published by the saga)
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

        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            sagaFinalized.Should().BeTrue();
            outboxMessages.Should().ContainMessageOfType<AuthorizePaymentCommand>(correlationId.ToString());
            outboxMessages.Should().ContainMessageOfType<CapturePaymentCommand>(correlationId.ToString());
            // ADR-0026: Payments owns the terminal PaymentCompletedEvent.
            outboxMessages.Where(om => om.Type == typeof(PaymentCompletedEvent).FullName)
                .Should().BeEmpty("Payments owns the terminal PaymentCompletedEvent, not the sub-saga");
        }
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
    public async Task WhenCaptureAborted_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // ADR-0026: the Checkout saga aborts (its confirmation failed); the sub-saga voids the
        // pre-capture authorization — a free void, never a refund.
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureApproval(correlationId, userId, authorizationId);

        // Act
        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            CreateAbortCaptureCommand(correlationId, userId, "Order confirmation failed"));

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
        }
    }

    [Fact]
    public async Task WhenCaptureFailsNonRetryable_ShouldTriggerVoidAndNotPublishTerminal()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCapture(correlationId, userId, authorizationId);

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
            // ADR-0026: Payments owns the terminal PaymentFailedEvent — the sub-saga must not outbox it.
            outboxMessages.Where(om => om.Type == typeof(PaymentFailedEvent).FullName)
                .Should().BeEmpty("Payments owns the terminal PaymentFailedEvent, not the sub-saga");
        }
    }

    [Fact]
    public async Task WhenVoidCompletes_ShouldFinalizeInVoidCompletedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

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
    public async Task WhenCaptureApprovalTimesOut_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // ADR-0026 wait-state timeout: the Checkout saga never signalled approval/abort, so the
        // dangling authorization is released via the void path.
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCaptureApproval(correlationId, userId, authorizationId);

        // Act - Simulate timeout by publishing CaptureApprovalTimeoutExpired (MassTransit internal)
        await Bus.Publish(new CaptureApprovalTimeoutExpired { CorrelationId = correlationId });

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
        }
    }

    [Fact]
    public async Task WhenCaptureTimesOut_ShouldTriggerVoidAndTransitionToVoidInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await TransitionSagaToAwaitingCapture(correlationId, userId, authorizationId);

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

    // -- Helper Methods --

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

    private ApproveCaptureCommand CreateApproveCaptureCommand(Guid correlationId, Guid userId) => new()
    {
        CorrelationId = correlationId,
        UserId = userId,
        RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
    };

    private AbortCaptureCommand CreateAbortCaptureCommand(Guid correlationId, Guid userId, string reason) => new()
    {
        CorrelationId = correlationId,
        UserId = userId,
        Reason = reason,
        RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
    };

    /// <summary>
    /// Drives the saga to <c>AwaitingCaptureApproval</c> (request → authorize). Per ADR-0026 the
    /// sub-saga parks here waiting for the Checkout saga's capture-approval / abort signal.
    /// </summary>
    private async Task TransitionSagaToAwaitingCaptureApproval(
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

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCaptureApproval, DefaultTimeout);
    }

    /// <summary>
    /// Drives the saga to <c>AwaitingCapture</c> (request → authorize → capture-approval). The
    /// Checkout saga issues capture approval only after confirming stock + order.
    /// </summary>
    private async Task TransitionSagaToAwaitingCapture(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToAwaitingCaptureApproval(correlationId, userId, authorizationId, amount, currency);

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            CreateApproveCaptureCommand(correlationId, userId));

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingCapture, DefaultTimeout);
    }

    /// <summary>
    /// Drives the saga to <c>VoidInProgress</c> via the abort path (request → authorize → abort).
    /// </summary>
    private async Task TransitionSagaToVoidInProgressState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToAwaitingCaptureApproval(correlationId, userId, authorizationId, amount, currency);

        await KafkaTestProducer.ProduceAsync(TopicsOptions.PaymentsPaymentCommands, userId,
            CreateAbortCaptureCommand(correlationId, userId, "Compensation: confirmation failed"));

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.VoidInProgress, DefaultTimeout);
    }

    /// <summary>
    /// Reads the saga's PaymentTransactionId from the persisted state. Must be called after the
    /// saga has reached at least <c>AwaitingAuthorization</c> (the Initial transition mints it
    /// per wave1-followup #255). Used by tests that echo the value back on a downstream capture
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
