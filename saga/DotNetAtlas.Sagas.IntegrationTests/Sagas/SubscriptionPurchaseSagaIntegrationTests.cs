using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using Finance.Payments;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using SubscriptionTier = Order.AlertSubscriptions.SubscriptionTier;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the SubscriptionPurchaseSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class SubscriptionPurchaseSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private ISagaStateMachineTestHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>
        SagaHarness { get; }

    public SubscriptionPurchaseSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness
            .GetSagaStateMachineHarness<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>();
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be("WaitingForPayment");
            persistedState.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            persistedState.DurationDays.Should().Be(30);
        }
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
        var activatedEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(activatedEvent);

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
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

        // Assert
        await SagaHarness.Exists(correlationId1, x => x.WaitingForPayment, DefaultTimeout);
        await SagaHarness.Exists(correlationId2, x => x.WaitingForPayment, DefaultTimeout);

        var state1 = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        using (new AssertionScope())
        {
            state1.Should().NotBeNull();
            state2.Should().NotBeNull();
            state1.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            state2.SubscriptionTier.Should().Be(SubscriptionTier.Ultra);
        }
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

        // Verify: WaitingForPayment state persisted
        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);
        var stateAfterInitiation = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterInitiation.Should().NotBeNull();
            stateAfterInitiation.CurrentState.Should().Be("WaitingForPayment");
            stateAfterInitiation.UserId.Should().Be(userId);
            stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
            stateAfterInitiation.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            stateAfterInitiation.DurationDays.Should().Be(30);
            stateAfterInitiation.Amount.Should().Be(29.99m);
            stateAfterInitiation.Currency.Should().Be("USD");
            stateAfterInitiation.PurchaseInitiatedAtUtc.Should().NotBe(default);
        }

        // Step 2: Complete payment
        var paymentCompletedEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 29.99m,
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);

        // Verify: AwaitingActivation state persisted
        await SagaHarness.Exists(correlationId, x => x.AwaitingActivation, DefaultTimeout);
        var stateAfterPayment = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterPayment.Should().NotBeNull();
            stateAfterPayment.CurrentState.Should().Be("AwaitingActivation");
            stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
            stateAfterPayment.PaymentCompletedAtUtc.Should().HaveValue();
        }

        // Step 3: Activate subscription
        var activatedEvent = new AlertSubscriptionActivatedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(activatedEvent);

        // Verify: ActivationCompleted - saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);

        // Act
        var paymentFailedEvent = new AlertSubscriptionPurchasePaymentFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "PAYMENT_DECLINED",
            ErrorMessage = "Payment was declined",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentFailedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionPurchasePaymentFailedSagaEvent>();

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
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
        var activationFailedEvent = new AlertSubscriptionActivationFailedSagaEvent
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when activation fails with ShouldCompensate=true");
            var outboxMessages = FakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
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
        var activationFailedEvent = new AlertSubscriptionActivationFailedSagaEvent
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
        await SagaHarness.Consumed.Any<AlertSubscriptionActivationFailedSagaEvent>();

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
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
        var compensationCompletedEvent = new AlertSubscriptionPurchaseCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = refundTransactionId,
            CompensatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(compensationCompletedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionPurchaseCompensationCompletedSagaEvent>();

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentTimesOut_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);

        // Act - Simulate timeout by publishing PaymentTimeoutExpired
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await TestHarness.Bus.Publish(timeoutEvent);
        await SagaHarness.Consumed.Any<PaymentTimeoutExpired>();

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionPurchaseSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when activation times out");
            var outboxMessages = FakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
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

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
    }

    private AlertSubscriptionPurchaseInitiatedSagaEvent CreatePurchaseInitiatedEvent(
        Guid correlationId,
        Guid userId,
        SubscriptionTier tier = SubscriptionTier.Pro,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new AlertSubscriptionPurchaseInitiatedSagaEvent
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
        var initiatedEvent = CreatePurchaseInitiatedEvent(
            correlationId, userId, tier, durationDays, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);

        var paymentCompletedEvent = new AlertSubscriptionPurchasePaymentCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = amount,
            Currency = currency,
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(paymentCompletedEvent);

        await SagaHarness.Exists(correlationId, x => x.AwaitingActivation, DefaultTimeout);
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
        await TransitionSagaToAwaitingActivationState(
            correlationId, userId, paymentTransactionId, tier, durationDays, amount, currency);

        var activationFailedEvent = new AlertSubscriptionActivationFailedSagaEvent
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

        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
    }
}
