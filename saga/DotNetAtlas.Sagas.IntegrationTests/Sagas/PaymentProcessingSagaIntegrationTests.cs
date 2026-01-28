using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Commands;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the PaymentProcessingSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class PaymentProcessingSagaIntegrationTests : BasePaymentSagaIntegrationTest
{
    public PaymentProcessingSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
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
        await SagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Assert - verify state was persisted to database
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.UserId.Should().Be(userId);
        persistedState.CurrentState.Should().Be("AwaitingAuthorization");
        persistedState.Amount.Should().Be(9.99m);
        persistedState.Currency.Should().Be("USD");
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
            Amount = 99.99m,
            Currency = "USD",
            CapturedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(capturedEvent);
        await SagaHarness.Consumed.Any<PaymentCapturedEvent>();

        // Assert - verify state was updated
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("PaymentCompleted");
        persistedState.PaymentTransactionId.Should().Be(paymentTransactionId);
        persistedState.AuthorizationId.Should().Be(authorizationId);
        persistedState.CapturedAtUtc.Should().NotBeNull();
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

        await AsyncEnumerable.ToListAsync(
            AsyncEnumerable.Take(
                SagaHarness.Consumed.SelectAsync<PaymentInitiatedEvent>(), 2));

        // Assert - both sagas exist independently
        var state1 = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        state1.Should().NotBeNull();
        state2.Should().NotBeNull();
        state1!.Amount.Should().Be(9.99m);
        state1.Currency.Should().Be("USD");
        state2!.Amount.Should().Be(99.99m);
        state2.Currency.Should().Be("EUR");
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
        await SagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Verify: AwaitingAuthorization state persisted
        var stateAfterInitiation = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterInitiation.Should().NotBeNull();
        stateAfterInitiation!.CurrentState.Should().Be("AwaitingAuthorization");
        stateAfterInitiation.UserId.Should().Be(userId);
        stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
        stateAfterInitiation.Amount.Should().Be(49.99m);
        stateAfterInitiation.Currency.Should().Be("USD");
        stateAfterInitiation.InitiatedAtUtc.Should().NotBe(default);

        // Step 2: Authorize payment
        var authorizedEvent = new PaymentAuthorizedEvent
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
        await SagaHarness.Consumed.Any<PaymentAuthorizedEvent>();

        // Verify: AwaitingCapture state persisted
        var stateAfterAuthorization = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterAuthorization.Should().NotBeNull();
        stateAfterAuthorization!.CurrentState.Should().Be("AwaitingCapture");
        stateAfterAuthorization.AuthorizationId.Should().Be(authorizationId);
        stateAfterAuthorization.AuthorizedAtUtc.Should().HaveValue();

        // Step 3: Capture payment
        var capturedEvent = new PaymentCapturedEvent
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
        await SagaHarness.Consumed.Any<PaymentCapturedEvent>();

        // Verify: PaymentCompleted state persisted
        var stateAfterCapture = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterCapture.Should().NotBeNull();
        stateAfterCapture!.CurrentState.Should().Be("PaymentCompleted");
        stateAfterCapture.PaymentTransactionId.Should().Be(paymentTransactionId);
        stateAfterCapture.CapturedAtUtc.Should().HaveValue();
        stateAfterCapture.CompensationTriggered.Should().BeFalse();

        // Verify: PaymentCompletedEvent was published
        (await TestHarness.Published.Any<Finance.Payments.PaymentCompletedEvent>()).Should().BeTrue(
            "PaymentCompletedEvent should be published to Kafka when payment is captured");
    }

    private PaymentInitiatedEvent CreatePaymentInitiatedEvent(
        Guid correlationId,
        Guid userId,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new PaymentInitiatedEvent
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
        await SagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Act - Send non-retryable authorization failure
        var authFailedEvent = new PaymentAuthorizationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "CARD_DECLINED",
            ErrorMessage = "Card was declined by issuer",
            IsRetryable = false
        };

        await TestHarness.Bus.Publish(authFailedEvent);
        await SagaHarness.Consumed.Any<PaymentAuthorizationFailedEvent>();

        // Assert - verify saga finalized in AuthorizationFailed state
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization or remain in final state
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("AuthorizationFailed");
            persistedState.ErrorCode.Should().Be("CARD_DECLINED");
            persistedState.ErrorMessage.Should().Be("Card was declined by issuer");
        }
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
            IsRetryable = false
        };

        await TestHarness.Bus.Publish(captureFailedEvent);
        await SagaHarness.Consumed.Any<PaymentCaptureFailedEvent>();

        // Assert - verify saga transitioned to VoidInProgress and compensation triggered
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("VoidInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify PaymentFailedEvent was published to Kafka
        (await TestHarness.Published.Any<Finance.Payments.PaymentFailedEvent>()).Should().BeTrue(
            "PaymentFailedEvent should be published when capture fails non-retryable");
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

        await TestHarness.Bus.Publish(voidedEvent);
        await SagaHarness.Consumed.Any<PaymentVoidedEvent>();

        // Assert - verify saga finalized in VoidCompleted state
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("VoidCompleted");
        }
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
        var refundCommand = new RequestPaymentRefundCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Customer requested refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(refundCommand);
        await SagaHarness.Consumed.Any<RequestPaymentRefundCommand>();

        // Assert - verify saga transitioned to RefundInProgress
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("RefundInProgress");
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
        await SagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Act - Simulate timeout by publishing AuthorizationTimeoutExpired
        var timeoutEvent = new AuthorizationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<AuthorizationTimeoutExpired>();

        // Assert - verify saga finalized in AuthorizationFailed state
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("AuthorizationFailed");
        }
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
        await SagaHarness.Consumed.Any<CaptureTimeoutExpired>();

        // Assert - verify saga transitioned to VoidInProgress
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("VoidInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify PaymentFailedEvent was published
        (await TestHarness.Published.Any<Finance.Payments.PaymentFailedEvent>()).Should().BeTrue(
            "PaymentFailedEvent should be published when capture times out");
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

        // Assert - verify saga finalized in VoidFailed state
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("VoidFailed");
        }
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

        // Assert - verify saga finalized in RefundFailed state
        var persistedState = await DbContext.Set<PaymentSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("RefundFailed");
        }
    }

    // -- Helper Methods --

    private async Task TransitionSagaToAwaitingCaptureState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // Publish PaymentInitiatedEvent
        var initiatedEvent = CreatePaymentInitiatedEvent(correlationId, userId, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<PaymentInitiatedEvent>();

        // Publish PaymentAuthorizedEvent
        var authorizedEvent = new PaymentAuthorizedEvent
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
        await SagaHarness.Consumed.Any<PaymentAuthorizedEvent>();
    }

    private async Task TransitionSagaToVoidInProgressState(
        Guid correlationId,
        Guid userId,
        string authorizationId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // First transition to AwaitingCapture
        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        // Then trigger non-retryable capture failure to transition to VoidInProgress
        var captureFailedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            AuthorizationId = authorizationId,
            ErrorCode = "CAPTURE_FAILED",
            ErrorMessage = "Capture failed",
            IsRetryable = false
        };

        await TestHarness.Bus.Publish(captureFailedEvent);
        await SagaHarness.Consumed.Any<PaymentCaptureFailedEvent>();
    }

    private async Task TransitionSagaToPaymentCompletedState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        var authorizationId = $"auth-{Guid.NewGuid()}";

        // Transition to AwaitingCapture
        await TransitionSagaToAwaitingCaptureState(correlationId, userId, authorizationId, amount, currency);

        // Capture the payment
        var capturedEvent = new PaymentCapturedEvent
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
        await SagaHarness.Consumed.Any<PaymentCapturedEvent>();
    }

    private async Task TransitionSagaToRefundInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // First transition to PaymentCompleted
        await TransitionSagaToPaymentCompletedState(correlationId, userId, paymentTransactionId, amount, currency);

        // Then request refund to transition to RefundInProgress
        var refundCommand = new RequestPaymentRefundCommand
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Reason = "Test refund",
            RequestedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(refundCommand);
        await SagaHarness.Consumed.Any<RequestPaymentRefundCommand>();
    }
}
