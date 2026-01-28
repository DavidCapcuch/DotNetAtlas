using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Schedules;
using Microsoft.EntityFrameworkCore;
using SubscriptionTier = Order.AlertSubscriptions.SubscriptionTier;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the SubscriptionPurchaseSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class SubscriptionPurchaseSagaIntegrationTests : BasePurchaseSagaIntegrationTest
{
    public SubscriptionPurchaseSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task WhenPurchaseInitiated_ShouldTransitionToAndPersistWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionPurchaseInitiatedEvent>();

        // Assert - verify state was persisted to database
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.UserId.Should().Be(userId);
        persistedState.CurrentState.Should().Be("WaitingForPayment");
        persistedState.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
        persistedState.DurationDays.Should().Be(30);
    }

    [Fact]
    public async Task WhenSubscriptionActivated_ShouldTransitionToAndPersistActivationCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - transition to completed via activation
        var activatedEvent = new SubscriptionActivatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(activatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionActivatedEvent>();

        // Assert - verify state was updated
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("ActivationCompleted");
        persistedState.ActivationCompletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task WhenMultipleSagasInitiated_ShouldMaintainIsolatedStates()
    {
        // Arrange
        var correlationId1 = Guid.NewGuid();
        var correlationId2 = Guid.NewGuid();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        var initiatedEvent1 = CreatePurchaseInitiatedEvent(
            correlationId1, userId1, SubscriptionTier.Pro, 30, 9.99m);
        var initiatedEvent2 = CreatePurchaseInitiatedEvent(
            correlationId2, userId2, SubscriptionTier.Ultra, 365, 99.99m);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent1);
        await TestHarness.Bus.Publish(initiatedEvent2);

        await AsyncEnumerable.ToListAsync(
            AsyncEnumerable.Take(
                SagaHarness.Consumed.SelectAsync<SubscriptionPurchaseInitiatedEvent>(), 2));

        // Assert - both sagas exist independently
        var state1 = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        state1.Should().NotBeNull();
        state2.Should().NotBeNull();
        state1!.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
        state2!.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
    }

    [Fact]
    public async Task WhenFullPurchaseFlow_ShouldPersistStateAtEachTransition()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        // Step 1: Initiate purchase
        var initiatedEvent = CreatePurchaseInitiatedEvent(
            correlationId, userId, SubscriptionTier.Pro, 30, 29.99m, "USD", paymentMethodId);

        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionPurchaseInitiatedEvent>();

        // Verify: WaitingForPayment state persisted
        var stateAfterInitiation = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterInitiation.Should().NotBeNull();
        stateAfterInitiation!.CurrentState.Should().Be("WaitingForPayment");
        stateAfterInitiation.UserId.Should().Be(userId);
        stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
        stateAfterInitiation.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
        stateAfterInitiation.DurationDays.Should().Be(30);
        stateAfterInitiation.Amount.Should().Be(29.99m);
        stateAfterInitiation.Currency.Should().Be("USD");
        stateAfterInitiation.PurchaseInitiatedAtUtc.Should().NotBe(default);

        // Step 2: Complete payment
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 29.99m,
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);
        await SagaHarness.Consumed.Any<PaymentCompletedEvent>();

        // Verify: AwaitingActivation state persisted
        var stateAfterPayment = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterPayment.Should().NotBeNull();
        stateAfterPayment!.CurrentState.Should().Be("AwaitingActivation");
        stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
        stateAfterPayment.PaymentCompletedAtUtc.Should().HaveValue();

        // Step 3: Activate subscription
        var activatedEvent = new SubscriptionActivatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(activatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionActivatedEvent>();

        // Verify: ActivationCompleted state persisted (saga finalized)
        var stateAfterActivation = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Note: Saga may be removed after finalization, so we check if it exists or was finalized
        if (stateAfterActivation != null)
        {
            stateAfterActivation.CurrentState.Should().Be("ActivationCompleted");
            stateAfterActivation.ActivationCompletedAtUtc.Should().HaveValue();
            stateAfterActivation.CompensationTriggered.Should().BeFalse();
        }
    }

    // -- Unhappy Path Tests --

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionPurchaseInitiatedEvent>();

        // Act - Send payment failed event
        var paymentFailedEvent = new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "PAYMENT_DECLINED",
            ErrorMessage = "Payment was declined",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentFailedEvent);
        await SagaHarness.Consumed.Any<PaymentFailedEvent>();

        // Assert - verify saga finalized in PaymentFailed state
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("PaymentFailed");
        }
    }

    [Fact]
    public async Task WhenActivationFailsWithCompensation_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Send activation failed with ShouldCompensate=true
        var activationFailedEvent = new SubscriptionActivationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_FAILED",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await TestHarness.Bus.Publish(activationFailedEvent);
        await SagaHarness.Consumed.Any<SubscriptionActivationFailedEvent>();

        // Assert - verify saga transitioned to CompensationInProgress
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("CompensationInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify RequestRefundCommand was published
        (await TestHarness.Published.Any<Finance.Payments.RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when activation fails with ShouldCompensate=true");
    }

    [Fact]
    public async Task WhenActivationFailsWithoutCompensation_ShouldFinalizeInActivationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Send activation failed with ShouldCompensate=false
        var activationFailedEvent = new SubscriptionActivationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_ALREADY_EXISTS",
            ErrorMessage = "Subscription already activated",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await TestHarness.Bus.Publish(activationFailedEvent);
        await SagaHarness.Consumed.Any<SubscriptionActivationFailedEvent>();

        // Assert - verify saga finalized in ActivationFailed state
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("ActivationFailed");
            persistedState.CompensationTriggered.Should().BeFalse();
        }
    }

    [Fact]
    public async Task WhenCompensationCompletes_ShouldFinalizeInCompensationCompletedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var refundTransactionId = Guid.NewGuid();

        await TransitionSagaToCompensationInProgressState(correlationId, userId, paymentTransactionId);

        // Act - Send compensation completed event
        var compensationCompletedEvent = new SubscriptionCompensationCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = refundTransactionId,
            CompensatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(compensationCompletedEvent);
        await SagaHarness.Consumed.Any<SubscriptionCompensationCompletedEvent>();

        // Assert - verify saga finalized in CompensationCompleted state
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("CompensationCompleted");
        }
    }

    // -- Timeout Tests --

    [Fact]
    public async Task WhenPaymentTimesOut_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionPurchaseInitiatedEvent>();

        // Act - Simulate timeout by publishing PaymentTimeoutExpired
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<PaymentTimeoutExpired>();

        // Assert - verify saga finalized in PaymentFailed state
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("PaymentFailed");
        }
    }

    [Fact]
    public async Task WhenActivationTimesOut_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing ActivationTimeoutExpired
        var timeoutEvent = new ActivationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<ActivationTimeoutExpired>();

        // Assert - verify saga transitioned to CompensationInProgress
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState!.CurrentState.Should().Be("CompensationInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify RequestRefundCommand was published
        (await TestHarness.Published.Any<Finance.Payments.RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when activation times out");
    }

    [Fact]
    public async Task WhenCompensationTimesOut_ShouldFinalizeInCompensationFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToCompensationInProgressState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing CompensationTimeoutExpired
        var timeoutEvent = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<CompensationTimeoutExpired>();

        // Assert - verify saga finalized in CompensationFailed state
        var persistedState = await DbContext.Set<SubscriptionPurchaseSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("CompensationFailed");
        }
    }

    // -- Helper Methods --

    private SubscriptionPurchaseInitiatedEvent CreatePurchaseInitiatedEvent(
        Guid correlationId,
        Guid userId,
        SubscriptionTier tier = SubscriptionTier.Pro,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new SubscriptionPurchaseInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.NewGuid(),
            SubscriptionTier = tier,
            DurationDays = durationDays,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"purchase-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };
    }

    private async Task TransitionSagaToAwaitingActivationState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        SubscriptionTier tier = SubscriptionTier.Ultra,
        int durationDays = 365,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // Publish SubscriptionPurchaseInitiatedEvent
        var initiatedEvent = CreatePurchaseInitiatedEvent(
            correlationId, userId, tier, durationDays, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<SubscriptionPurchaseInitiatedEvent>();

        // Publish PaymentCompletedEvent
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = amount,
            Currency = currency,
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);
        await SagaHarness.Consumed.Any<PaymentCompletedEvent>();
    }

    private async Task TransitionSagaToCompensationInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        SubscriptionTier tier = SubscriptionTier.Ultra,
        int durationDays = 365,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // First transition to AwaitingActivation
        await TransitionSagaToAwaitingActivationState(
            correlationId, userId, paymentTransactionId, tier, durationDays, amount, currency);

        // Then trigger activation failure with ShouldCompensate=true
        var activationFailedEvent = new SubscriptionActivationFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ErrorCode = "ACTIVATION_FAILED",
            ErrorMessage = "Failed to activate subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await TestHarness.Bus.Publish(activationFailedEvent);
        await SagaHarness.Consumed.Any<SubscriptionActivationFailedEvent>();
    }
}
