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
/// Unit tests for the PaymentProcessingSaga state machine after the ADR-0026 capture-pivot
/// restructure. Tests verify correct state transitions, the AwaitingCaptureApproval wait-state,
/// the capture-approval / abort handshake, timeout scenarios, and compensation logic. Per ADR-0029
/// the saga is keyed on OrderId (<c>CorrelationId == OrderId</c>); every internal saga event
/// correlates on OrderId.
/// </summary>
/// <remarks>
/// The saga flow is:
/// 1. PaymentInitiatedEvent → AwaitingAuthorization (publishes AuthorizePaymentCommand)
/// 2. PaymentAuthorizedEvent → AwaitingCaptureApproval (does NOT capture yet — waits for the
///    Checkout saga to confirm stock + order and signal capture approval)
/// 3a. ApproveCaptureSagaEvent → AwaitingCapture (publishes CapturePaymentCommand)
/// 3b. AbortCaptureSagaEvent / CaptureApprovalTimeout → VoidInProgress (publishes VoidPaymentCommand)
/// 4. PaymentCapturedEvent → PaymentCompleted + finalized. The saga does NOT publish the terminal
///    PaymentCompletedEvent — per ADR-0026 the Payments service owns its terminal events.
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
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
            OrderId = orderId,
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
        (await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>()).Should().BeTrue();
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var awaitingAuthorizationSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            awaitingAuthorizationSagaState.Should().NotBeNull();
            // ADR-0029: the saga is keyed on OrderId — its CorrelationId equals the OrderId.
            awaitingAuthorizationSagaState.CorrelationId.Should().Be(orderId);
            awaitingAuthorizationSagaState.OrderId.Should().Be(orderId);
            awaitingAuthorizationSagaState.UserId.Should().Be(userId);
            awaitingAuthorizationSagaState.Amount.Should().Be(9.99m);
            awaitingAuthorizationSagaState.Currency.Should().Be("USD");
        }
    }

    [Fact]
    public async Task WhenPaymentAuthorized_ShouldTransitionToAwaitingCaptureApproval()
    {
        // ADR-0026: capture is deferred to the pivot. On authorization the sub-saga parks in
        // AwaitingCaptureApproval and waits for the Checkout saga's capture-approval / abort
        // signal — it must NOT issue CapturePaymentCommand here.
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            OrderId = orderId,
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

        var awaitingApprovalSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCaptureApproval);

        using (new AssertionScope())
        {
            awaitingApprovalSagaState.Should().NotBeNull("Saga should be in AwaitingCaptureApproval state");
            awaitingApprovalSagaState.AuthorizationId.Should().Be(authorizationId);
            // Capture must NOT be triggered yet — it waits for the Checkout saga's approval.
            _fakeOutboxWriter.HasMessage<CapturePaymentCommand>().Should().BeFalse(
                "capture is deferred to the pivot — no CapturePaymentCommand until approval");
        }
    }

    [Fact]
    public async Task WhenCaptureApproved_ShouldIssueCapture_AndTransitionToAwaitingCapture()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForCaptureApproval(orderId, userId, authorizationId);

        // Act
        await _testHarness.Bus.Publish(new ApproveCaptureSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        });

        // Assert
        (await _sagaHarness.Consumed.Any<ApproveCaptureSagaEvent>()).Should().BeTrue();

        var awaitingCaptureSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCapture);

        using (new AssertionScope())
        {
            awaitingCaptureSagaState.Should().NotBeNull("Saga should be in AwaitingCapture after approval");
            var captureCommands = _fakeOutboxWriter.GetMessages<CapturePaymentCommand>().ToList();
            captureCommands.Should().ContainSingle(
                "approval triggers exactly one CapturePaymentCommand to Payments");
            captureCommands[0].IntegrationEvent.AuthorizationId.Should().Be(authorizationId);
        }
    }

    [Fact]
    public async Task WhenCaptureAborted_ShouldIssueVoid_AndTransitionToVoidInProgress()
    {
        // ADR-0026: on confirmation failure the Checkout saga sends AbortCapture; the sub-saga
        // voids the (pre-capture) authorization — a free void, never a refund.
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForCaptureApproval(orderId, userId, authorizationId);

        // Act
        await _testHarness.Bus.Publish(new AbortCaptureSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            Reason = "Order confirmation failed",
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        });

        // Assert
        (await _sagaHarness.Consumed.Any<AbortCaptureSagaEvent>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull("Saga should transition to VoidInProgress on abort");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
            var voidCommands = _fakeOutboxWriter.GetMessages<VoidPaymentCommand>().ToList();
            voidCommands.Should().ContainSingle("abort triggers exactly one VoidPaymentCommand");
            voidCommands[0].IntegrationEvent.AuthorizationId.Should().Be(authorizationId);
        }
    }

    [Fact]
    public async Task WhenCaptureApprovalTimeout_ShouldIssueVoid_AndTransitionToVoidInProgress()
    {
        // ADR-0026 risk mitigation: if the capture-approval signal never arrives (Checkout saga
        // crashed after authorize), the wait-state timeout drives the void path so the
        // authorization is released rather than left dangling.
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishAndWaitForCaptureApproval(orderId, userId, authorizationId);

        // Act
        await _testHarness.Bus.Publish(new CaptureApprovalTimeoutExpired
        {
            CorrelationId = orderId
        });

        // Assert
        (await _sagaHarness.Consumed.Any<CaptureApprovalTimeoutExpired>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull(
                "Saga should transition to VoidInProgress on capture-approval timeout");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
            _fakeOutboxWriter.HasMessage<VoidPaymentCommand>().Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldFinalizeInPaymentCompleted()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishApprovedAndWaitForAwaitingCapture(orderId, userId, authorizationId);

        // Wave1-followup #255: PaymentTransactionId is minted by the saga in Initial state and
        // echoed back by Payments on PaymentCapturedEvent. The saga throws on mismatch instead of
        // overwriting, so the test must read the saga's minted value rather than fabricating one.
        var sagaMintedPaymentTransactionId = GetSagaMintedPaymentTransactionId(orderId);

        // Act
        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            PaymentTransactionId = sagaMintedPaymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCapturedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCapturedSagaEvent>()).Should().BeTrue();

        // The sub-saga reaches its successful terminal (PaymentCompleted) and finalizes — it no
        // longer lingers to await refund requests (refund is a deferred customer/admin flow).
        var sagaNotExists = await _sagaHarness.NotExists(orderId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should finalize after capture completes");
    }

    [Fact]
    public async Task WhenPaymentCaptured_ShouldNotPublishPaymentCompletedEvent_PaymentsOwnsTerminal()
    {
        // ADR-0026: the Payments service (not the sub-saga) is the authoritative producer of the
        // terminal PaymentCompletedEvent. The sub-saga orchestrates only — it must NOT publish it.
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishApprovedAndWaitForAwaitingCapture(orderId, userId, authorizationId);
        var sagaMintedPaymentTransactionId = GetSagaMintedPaymentTransactionId(orderId);

        // Act
        var paymentCapturedSagaEvent = new PaymentCapturedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            PaymentTransactionId = sagaMintedPaymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            CapturedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCapturedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentCapturedSagaEvent>();

        // Assert
        _fakeOutboxWriter.HasMessage<PaymentCompletedEvent>().Should().BeFalse(
            "Payments owns the terminal PaymentCompletedEvent (ADR-0026); the sub-saga must not publish it");
    }

    [Fact]
    public async Task WhenAuthorizationFailed_NonRetryable_ShouldFinalizeAndNotPublishPaymentFailedEvent()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var paymentAuthorizationFailedSagaEvent = new PaymentAuthorizationFailedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card declined",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentAuthorizationFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentAuthorizationFailedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(orderId, timeout: DefaultTimeout) is null;

        using (new AssertionScope())
        {
            sagaNotExists.Should().BeTrue("Saga should be finalized after non-retryable auth failure");
            // ADR-0026: Payments already published PaymentFailedEvent on the decline (it owns the
            // terminal); the sub-saga must not publish a payment-state event of its own.
            _fakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeFalse(
                "Payments owns the terminal PaymentFailedEvent; the sub-saga must not publish it");
        }
    }

    [Fact]
    public async Task WhenCaptureFailed_NonRetryable_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishApprovedAndWaitForAwaitingCapture(orderId, userId, authorizationId);

        // Act
        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            AuthorizationId = authorizationId,
            // Upstream-owned code emitted by the Payments BC's gateway adapter on PaymentCaptureFailedEvent.ErrorCode;
            // not extracted to PaymentProcessingSagaErrorCodes because saga is a consumer of this vocabulary, not the owner.
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Unable to capture funds",
            IsRetryable = false,
            FailedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentCaptureFailedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentCaptureFailedSagaEvent>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull("Saga should transition to VoidInProgress after capture failure");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
            // Payments owns the terminal PaymentFailedEvent (ADR-0026); the sub-saga only voids.
            _fakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeFalse();
        }
    }

    [Fact]
    public async Task WhenPaymentVoided_ShouldTransitionToVoidCompleted()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishApprovedAndWaitForAwaitingCapture(orderId, userId, authorizationId);

        // Fail capture to get to VoidInProgress
        var paymentCaptureFailedSagaEvent = new PaymentCaptureFailedSagaEvent
        {
            OrderId = orderId,
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
            OrderId = orderId,
            UserId = userId,
            AuthorizationId = authorizationId,
            VoidedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentVoidedSagaEvent);

        // Assert
        (await _sagaHarness.Consumed.Any<PaymentVoidedSagaEvent>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(orderId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after void completed");
    }

    [Fact]
    public async Task WhenAuthorizationTimeout_ShouldTransitionToAuthorizationFailed()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Act
        var authorizationTimeoutExpired = new AuthorizationTimeoutExpired
        {
            CorrelationId = orderId
        };

        await _testHarness.Bus.Publish(authorizationTimeoutExpired);

        // Assert
        (await _sagaHarness.Consumed.Any<AuthorizationTimeoutExpired>()).Should().BeTrue();

        var sagaNotExists = await _sagaHarness.NotExists(orderId, timeout: DefaultTimeout) is null;
        sagaNotExists.Should().BeTrue("Saga should be finalized after authorization timeout");
    }

    [Fact]
    public async Task WhenCaptureTimeout_ShouldTransitionToVoidInProgress()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";

        await PublishApprovedAndWaitForAwaitingCapture(orderId, userId, authorizationId);

        // Act
        var captureTimeoutExpired = new CaptureTimeoutExpired
        {
            CorrelationId = orderId
        };

        await _testHarness.Bus.Publish(captureTimeoutExpired);

        // Assert
        (await _sagaHarness.Consumed.Any<CaptureTimeoutExpired>()).Should().BeTrue();

        var voidInProgressSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.VoidInProgress);

        using (new AssertionScope())
        {
            voidInProgressSagaState.Should().NotBeNull("Saga should be in VoidInProgress after capture timeout");
            voidInProgressSagaState.CompensationTriggered.Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldPublishAuthorizePaymentCommand()
    {
        // Arrange
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = $"pm_{Guid.CreateVersion7():N}";

        var paymentInitiatedSagaEvent = new PaymentInitiatedSagaEvent
        {
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
            // ADR-0029: CorrelationId == OrderId; the outbound command carries both as the same value.
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
        // distinct from the saga key (OrderId); the v1 collapse where they coincided is being unwound.
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);

        // Act
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        (await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>()).Should().BeTrue();
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        // Assert
        var awaitingAuthorizationSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            awaitingAuthorizationSagaState.Should().NotBeNull();
            awaitingAuthorizationSagaState.PaymentTransactionId.Should().NotBeNull(
                "the saga issues PaymentTransactionId up front per wave1-followup #255");
            awaitingAuthorizationSagaState.PaymentTransactionId!.Value.Should().NotBeEmpty();
            awaitingAuthorizationSagaState.PaymentTransactionId.Value.Should().NotBe(orderId,
                "PaymentTransactionId must be distinct from the saga key (OrderId) — no v1 collapse");
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
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);

        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>();

        var outboxMessage = _fakeOutboxWriter.GetMessages<AuthorizePaymentCommand>().Single();
        var sagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);

        using (new AssertionScope())
        {
            outboxMessage.IntegrationEvent.PaymentTransactionId.Should().Be(
                sagaState.PaymentTransactionId!.Value,
                "Avro PaymentTransactionId matches the saga-state value the Payments aggregate will adopt as its PK");
            outboxMessage.IntegrationEvent.PaymentTransactionId.Should().NotBe(orderId,
                "PaymentTransactionId must be distinct from the saga key (OrderId) on the wire");
        }
    }

    [Fact]
    public async Task WhenAuthorizationFailedRetryable_RetriedAuthorizeCommand_ReusesPaymentTransactionId()
    {
        // Cross-cutting wave1-followup #255:
        // The saga must stick with the originally-minted PaymentTransactionId on retry so the
        // Payments aggregate can identify the existing row (one-payment-per-order, idempotent retry).
        var orderId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await _testHarness.Bus.Publish(CreatePaymentInitiatedEvent(orderId, userId));
        (await _sagaHarness.Consumed.Any<PaymentInitiatedSagaEvent>()).Should().BeTrue();
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var initialState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingAuthorization);
        var originalPaymentTransactionId = initialState.PaymentTransactionId;

        // Act: retryable auth failure → saga must republish AuthorizePaymentCommand
        await _testHarness.Bus.Publish(new PaymentAuthorizationFailedSagaEvent
        {
            OrderId = orderId,
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
        Guid orderId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD")
    {
        return new PaymentInitiatedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            PaymentMethodId = $"pm_{Guid.CreateVersion7():N}",
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.CreateVersion7()}",
            InitiatedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        };
    }

    /// <summary>
    /// Drives the saga to <c>AwaitingCaptureApproval</c> (initiate → authorize). Per ADR-0026 the
    /// sub-saga parks here waiting for the Checkout saga's capture-approval / abort signal.
    /// </summary>
    private async Task PublishAndWaitForCaptureApproval(
        Guid orderId,
        Guid userId,
        string authorizationId)
    {
        var paymentInitiatedSagaEvent = CreatePaymentInitiatedEvent(orderId, userId);
        await _testHarness.Bus.Publish(paymentInitiatedSagaEvent);
        var sagaExists = await _sagaHarness.Exists(orderId, timeout: DefaultTimeout) is not null;
        sagaExists.Should().BeTrue();

        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 9.99m,
            Currency = "USD",
            AuthorizedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = _fakeTimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await _testHarness.Bus.Publish(paymentAuthorizedSagaEvent);
        await _sagaHarness.Consumed.Any<PaymentAuthorizedSagaEvent>();

        var awaitingApprovalSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCaptureApproval);
        awaitingApprovalSagaState.Should().NotBeNull("Saga should be in AwaitingCaptureApproval state");
    }

    /// <summary>
    /// Drives the saga to <c>AwaitingCapture</c> (initiate → authorize → capture-approval). The
    /// Checkout saga issues capture approval only after confirming stock + order.
    /// </summary>
    private async Task PublishApprovedAndWaitForAwaitingCapture(
        Guid orderId,
        Guid userId,
        string authorizationId)
    {
        await PublishAndWaitForCaptureApproval(orderId, userId, authorizationId);

        await _testHarness.Bus.Publish(new ApproveCaptureSagaEvent
        {
            OrderId = orderId,
            UserId = userId,
            RequestedAtUtc = _fakeTimeProvider.GetUtcNow().UtcDateTime
        });
        await _sagaHarness.Consumed.Any<ApproveCaptureSagaEvent>();

        var awaitingCaptureSagaState = _sagaHarness.Sagas.ContainsInState(
            orderId, _sagaHarness.StateMachine, _sagaHarness.StateMachine.AwaitingCapture);
        awaitingCaptureSagaState.Should().NotBeNull("Saga should be in AwaitingCapture after approval");
    }

    /// <summary>
    /// Reads the saga's PaymentTransactionId from the in-memory test harness state regardless of
    /// the current state. The saga mints this in <c>Initial</c> (wave1-followup #255); callers
    /// need it whenever they construct a downstream event whose PaymentTransactionId must echo it.
    /// </summary>
    private Guid GetSagaMintedPaymentTransactionId(Guid orderId)
    {
        var saga = _sagaHarness.Sagas.Contains(orderId);
        saga.Should().NotBeNull("Saga must exist to read the minted PaymentTransactionId");
        return saga.PaymentTransactionId
            ?? throw new InvalidOperationException(
                $"Saga {orderId} has no minted PaymentTransactionId — "
                + "wave1-followup #255 invariant violation");
    }
}
