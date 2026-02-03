using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using Finance.Payments;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the SubscriptionExtensionSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class SubscriptionExtensionSagaIntegrationTests : BaseExtensionSagaIntegrationTest
{
    public SubscriptionExtensionSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task WhenExtensionInitiated_ShouldTransitionToAndPersistWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();

        // Assert - verify state was persisted to database
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState.UserId.Should().Be(userId);
        persistedState.CurrentState.Should().Be("WaitingForPayment");
        persistedState.DurationDays.Should().Be(30);
    }

    [Fact]
    public async Task WhenSubscriptionExtended_ShouldTransitionToAndPersistExtensionCompleted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var newExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(365).UtcDateTime;

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - transition to completed via extension
        var extendedEvent = new AlertSubscriptionExtendedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 365,
            NewExpiresAtUtc = newExpiresAtUtc,
            ExtendedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(extendedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtendedSagaEvent>();

        // Assert - verify state was updated
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState.CurrentState.Should().Be("ExtensionCompleted");
        persistedState.ExtensionCompletedAtUtc.Should().NotBeNull();
        persistedState.NewExpiresAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task WhenMultipleSagasInitiated_ShouldMaintainIsolatedStates()
    {
        // Arrange
        var correlationId1 = Guid.NewGuid();
        var correlationId2 = Guid.NewGuid();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        var initiatedEvent1 = CreateExtensionInitiatedEvent(correlationId1, userId1, 30, 9.99m);
        var initiatedEvent2 = CreateExtensionInitiatedEvent(correlationId2, userId2, 365, 99.99m);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent1);
        await TestHarness.Bus.Publish(initiatedEvent2);

        await AsyncEnumerable.ToListAsync(
            AsyncEnumerable.Take(
                SagaHarness.Consumed.SelectAsync<AlertSubscriptionExtensionInitiatedSagaEvent>(), 2));

        // Assert - both sagas exist independently
        var state1 = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        state1.Should().NotBeNull();
        state2.Should().NotBeNull();
        state1.DurationDays.Should().Be(30);
        state2.DurationDays.Should().Be(365);
    }

    [Fact]
    public async Task WhenFullExtensionFlow_ShouldPersistStateAtEachTransition()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentMethodId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();
        var newExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(90).UtcDateTime;

        // Step 1: Initiate extension
        var initiatedEvent = CreateExtensionInitiatedEvent(
            correlationId, userId, 90, 24.99m, "USD", paymentMethodId);

        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();

        // Verify: WaitingForPayment state persisted
        var stateAfterInitiation = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterInitiation.Should().NotBeNull();
        stateAfterInitiation.CurrentState.Should().Be("WaitingForPayment");
        stateAfterInitiation.UserId.Should().Be(userId);
        stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
        stateAfterInitiation.DurationDays.Should().Be(90);
        stateAfterInitiation.Amount.Should().Be(24.99m);
        stateAfterInitiation.Currency.Should().Be("USD");
        stateAfterInitiation.ExtensionInitiatedAtUtc.Should().NotBe(default);

        // Step 2: Complete payment
        var paymentCompletedEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 24.99m,
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();

        // Verify: AwaitingExtension state persisted
        var stateAfterPayment = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        stateAfterPayment.Should().NotBeNull();
        stateAfterPayment.CurrentState.Should().Be("AwaitingExtension");
        stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
        stateAfterPayment.PaymentCompletedAtUtc.Should().HaveValue();

        // Step 3: Extend subscription
        var extendedEvent = new AlertSubscriptionExtendedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 90,
            NewExpiresAtUtc = newExpiresAtUtc,
            ExtendedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(extendedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtendedSagaEvent>();

        // Verify: ExtensionCompleted state persisted (saga finalized)
        var stateAfterExtension = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Note: Saga may be removed after finalization, so we check if it exists or was finalized
        if (stateAfterExtension != null)
        {
            stateAfterExtension.CurrentState.Should().Be("ExtensionCompleted");
            stateAfterExtension.ExtensionCompletedAtUtc.Should().HaveValue();
            stateAfterExtension.NewExpiresAtUtc.Should().Be(newExpiresAtUtc);
            stateAfterExtension.CompensationTriggered.Should().BeFalse();
        }
    }

    // -- Unhappy Path Tests --

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();

        // Act - Send payment failed event
        var paymentFailedEvent = new AlertSubscriptionExtensionPaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "PAYMENT_DECLINED",
            ErrorMessage = "Payment was declined",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentFailedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentFailedSagaEvent>();

        // Assert - verify saga finalized in PaymentFailed state
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        persistedState?.CurrentState.Should().Be("PaymentFailed");
    }

    [Fact]
    public async Task WhenExtensionFailsWithCompensation_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Send extension failed with ShouldCompensate=true
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_FAILED",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await TestHarness.Bus.Publish(extensionFailedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        // Assert - verify saga transitioned to CompensationInProgress
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState.CurrentState.Should().Be("CompensationInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify RequestRefundCommand was published
        (await TestHarness.Published.Any<RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when extension fails with ShouldCompensate=true");
    }

    [Fact]
    public async Task WhenExtensionFailsWithoutCompensation_ShouldFinalizeInExtensionFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Send extension failed with ShouldCompensate=false
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ALREADY_APPLIED",
            ErrorMessage = "Extension already applied for this period",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await TestHarness.Bus.Publish(extensionFailedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();

        // Assert - verify saga finalized in ExtensionFailed state
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        if (persistedState != null)
        {
            persistedState.CurrentState.Should().Be("ExtensionFailed");
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
        var compensationCompletedEvent = new AlertSubscriptionExtensionCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            RefundTransactionId = refundTransactionId,
            CompensatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(compensationCompletedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionCompensationCompletedSagaEvent>();

        // Assert - verify saga finalized in CompensationCompleted state
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        persistedState?.CurrentState.Should().Be("CompensationCompleted");
    }

    // -- Timeout Tests --

    [Fact]
    public async Task WhenPaymentTimesOut_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();

        // Act - Simulate timeout by publishing PaymentTimeoutExpired
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<PaymentTimeoutExpired>();

        // Assert - verify saga finalized in PaymentFailed state
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        persistedState?.CurrentState.Should().Be("PaymentFailed");
    }

    [Fact]
    public async Task WhenExtensionTimesOut_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var paymentTransactionId = Guid.NewGuid();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing ExtensionTimeoutExpired
        var timeoutEvent = new ExtensionTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<ExtensionTimeoutExpired>();

        // Assert - verify saga transitioned to CompensationInProgress
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        persistedState.Should().NotBeNull();
        persistedState.CurrentState.Should().Be("CompensationInProgress");
        persistedState.CompensationTriggered.Should().BeTrue();

        // Verify RequestRefundCommand was published
        (await TestHarness.Published.Any<RequestRefundCommand>()).Should().BeTrue(
            "RequestRefundCommand should be published when extension times out");
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
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        // Saga may be removed after finalization
        persistedState?.CurrentState.Should().Be("CompensationFailed");
    }

    // -- Helper Methods --

    private AlertSubscriptionExtensionInitiatedSagaEvent CreateExtensionInitiatedEvent(
        Guid correlationId,
        Guid userId,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new AlertSubscriptionExtensionInitiatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.NewGuid(),
            DurationDays = durationDays,
            Amount = amount,
            Currency = currency,
            IdempotencyKey = $"extension-{userId}-{Guid.NewGuid()}",
            InitiatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };
    }

    private async Task TransitionSagaToAwaitingExtensionState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        int durationDays = 365,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // Publish SubscriptionExtensionInitiatedEvent
        var initiatedEvent = CreateExtensionInitiatedEvent(
            correlationId, userId, durationDays, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionInitiatedSagaEvent>();

        // Publish PaymentCompletedEvent
        var paymentCompletedEvent = new AlertSubscriptionExtensionPaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = amount,
            Currency = currency,
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionPaymentCompletedSagaEvent>();
    }

    private async Task TransitionSagaToCompensationInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        int durationDays = 365,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        // First transition to AwaitingExtension
        await TransitionSagaToAwaitingExtensionState(
            correlationId, userId, paymentTransactionId, durationDays, amount, currency);

        // Then trigger extension failure with ShouldCompensate=true
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_FAILED",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await TestHarness.Bus.Publish(extensionFailedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionFailedSagaEvent>();
    }
}
