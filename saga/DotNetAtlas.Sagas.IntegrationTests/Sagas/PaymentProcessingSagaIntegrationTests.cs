using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using DotNetAtlas.Sagas.IntegrationTests.Common;
using Finance.Payments;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the PaymentProcessingSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class PaymentProcessingSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private ISagaStateMachineTestHarness<PaymentProcessingSaga, PaymentProcessingSagaState> SagaHarness { get; }

    public PaymentProcessingSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness.GetSagaStateMachineHarness<PaymentProcessingSaga, PaymentProcessingSagaState>();
    }

    [Fact]
    public async Task WhenPaymentInitiated_ShouldTransitionToAndPersistAwaitingAuthorization()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent);

        // Assert
        await SagaHarness.Exists(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);
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
        var capturedEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 99.99m,
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(capturedEvent);

        // Assert
        await SagaHarness.Exists(correlationId, x => x.PaymentCompleted, DefaultTimeout);
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

        var initiatedEvent1 = CreatePaymentInitiatedEvent(correlationId1, userId1, 9.99m, "USD");
        var initiatedEvent2 = CreatePaymentInitiatedEvent(correlationId2, userId2, 99.99m, "EUR");

        // Act
        await TestHarness.Bus.Publish(initiatedEvent1);
        await TestHarness.Bus.Publish(initiatedEvent2);

        // Assert
        await SagaHarness.Exists(correlationId1, x => x.AwaitingAuthorization, DefaultTimeout);
        await SagaHarness.Exists(correlationId2, x => x.AwaitingAuthorization, DefaultTimeout);

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
        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId, 49.99m, "USD", paymentMethodId);

        await TestHarness.Bus.Publish(initiatedEvent);

        // Verify: AwaitingAuthorization state persisted
        await SagaHarness.Exists(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);
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
        var authorizedEvent = new PaymentAuthorizedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = 49.99m,
            Currency = "USD",
            AuthorizedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await TestHarness.Bus.Publish(authorizedEvent);

        // Verify: AwaitingCapture state persisted
        await SagaHarness.Exists(correlationId, x => x.AwaitingCapture, DefaultTimeout);
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
        var capturedEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = 49.99m,
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(capturedEvent);

        // Verify: PaymentCompleted state persisted
        await SagaHarness.Exists(correlationId, x => x.PaymentCompleted, DefaultTimeout);
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
        }

        // Verify: PaymentCompletedEvent was added to the outbox for publishing to Kafka
        using (new AssertionScope())
        {
            FakeOutboxWriter.HasMessage<PaymentCompletedEvent>().Should().BeTrue(
                "PaymentCompletedEvent should be added to the outbox for publishing to Kafka when payment is captured");

            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentCompletedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages.First().IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
    }

    private PaymentInitiatedSagaEvent CreatePaymentInitiatedEvent(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new PaymentInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.NewGuid(),
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"payment-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };
    }

    // -- Unhappy Path Tests --

    [Fact]
    public async Task WhenAuthorizationFailsNonRetryable_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);

        // Act - Send non-retryable authorization failure
        var authFailedEvent = new PaymentAuthorizationFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card was declined by issuer",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(authFailedEvent);
        await SagaHarness.Consumed.Any<PaymentAuthorizationFailedSagaEvent>();

        // Assert - verify saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
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
        var captureFailedEvent = new PaymentCaptureFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed permanently",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(captureFailedEvent);

        // Assert
        await SagaHarness.Exists(correlationId, x => x.VoidInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("VoidInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();
        }

        // Verify PaymentFailedEvent was added to the outbox for publishing to Kafka
        using (new AssertionScope())
        {
            FakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeTrue(
                "PaymentFailedEvent should be added to the outbox when capture fails non-retryable");

            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentFailedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages.First().IntegrationEvent.CorrelationId.Should().Be(correlationId);
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
        var voidedEvent = new PaymentVoidedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            VoidedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(voidedEvent);
        await SagaHarness.Consumed.Any<PaymentVoidedSagaEvent>();

        // Assert - verify saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
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

        // Act - Request refund
        var refundCommand = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(refundCommand);

        // Assert
        await SagaHarness.Exists(correlationId, x => x.RefundInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("RefundInProgress");
        }
    }

    // -- Timeout Tests --

    [Fact]
    public async Task WhenAuthorizationTimesOut_ShouldFinalizeInAuthorizationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);

        // Act - Simulate timeout by publishing AuthorizationTimeoutExpired
        var timeoutEvent = new AuthorizationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<AuthorizationTimeoutExpired>();

        // Assert - verify saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
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

        // Act - Simulate timeout by publishing CaptureTimeoutExpired
        var timeoutEvent = new CaptureTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);

        // Assert
        await SagaHarness.Exists(correlationId, x => x.VoidInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<PaymentProcessingSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("VoidInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();
        }

        // Verify PaymentFailedEvent was added to the outbox for publishing to Kafka
        using (new AssertionScope())
        {
            FakeOutboxWriter.HasMessage<PaymentFailedEvent>().Should().BeTrue(
                "PaymentFailedEvent should be added to the outbox when capture times out");

            var outboxMessages = FakeOutboxWriter.GetMessages<PaymentFailedEvent>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages.First().IntegrationEvent.CorrelationId.Should().Be(correlationId);
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

        // Act - Simulate timeout by publishing VoidTimeoutExpired
        var timeoutEvent = new VoidTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<VoidTimeoutExpired>();

        // Assert - verify saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
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

        // Act - Simulate timeout by publishing RefundTimeoutExpired
        var timeoutEvent = new RefundTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<RefundTimeoutExpired>();

        // Assert - verify saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
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
        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.AwaitingAuthorization, DefaultTimeout);

        var authorizedEvent = new PaymentAuthorizedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            Amount = amount,
            Currency = currency,
            AuthorizedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(7).UtcDateTime
        };

        await TestHarness.Bus.Publish(authorizedEvent);

        await SagaHarness.Exists(correlationId, x => x.AwaitingCapture, DefaultTimeout);
    }

    private async Task TransitionSagaToVoidInProgressState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        var captureFailedEvent = new PaymentCaptureFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed",
            IsRetryable = false,
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(captureFailedEvent);

        await SagaHarness.Exists(correlationId, x => x.VoidInProgress, DefaultTimeout);
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

        var capturedEvent = new PaymentCapturedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            AuthorizationId = authorizationId,
            Amount = amount,
            Currency = currency,
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(capturedEvent);

        await SagaHarness.Exists(correlationId, x => x.PaymentCompleted, DefaultTimeout);
    }

    private async Task TransitionSagaToRefundInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToPaymentCompletedState(correlationId, userId, paymentTransactionId, amount, currency);

        var refundCommand = new PaymentRefundRequestedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Test refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(refundCommand);

        await SagaHarness.Exists(correlationId, x => x.RefundInProgress, DefaultTimeout);
    }
}
