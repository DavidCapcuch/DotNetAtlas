using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using Finance.Payments;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

/// <summary>
/// Integration tests for the SubscriptionExtensionSaga state machine.
/// Tests verify saga state persistence, state transitions, and isolation using EF Core and real SQL Server via TestContainers.
/// </summary>
[Collection(nameof(SagaTestCollection))]
public class SubscriptionExtensionSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private ISagaStateMachineTestHarness<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>
        SagaHarness { get; }

    public SubscriptionExtensionSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaHarness = TestHarness
            .GetSagaStateMachineHarness<AlertSubscriptionExtensionSaga, AlertSubscriptionExtensionSagaState>();
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be("WaitingForPayment");
            persistedState.DurationDays.Should().Be(30);
        }
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

        // Act - transition to completed state via extension
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

        var initiatedEvent1 = CreateExtensionInitiatedEvent(correlationId1, userId1, 30, 9.99m);
        var initiatedEvent2 = CreateExtensionInitiatedEvent(correlationId2, userId2, 365, 99.99m);

        // Act
        await TestHarness.Bus.Publish(initiatedEvent1);
        await TestHarness.Bus.Publish(initiatedEvent2);

        // Assert
        await SagaHarness.Exists(correlationId1, x => x.WaitingForPayment, DefaultTimeout);
        await SagaHarness.Exists(correlationId2, x => x.WaitingForPayment, DefaultTimeout);

        var state1 = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId2);

        using (new AssertionScope())
        {
            state1.Should().NotBeNull();
            state2.Should().NotBeNull();
            state1.DurationDays.Should().Be(30);
            state2.DurationDays.Should().Be(365);
        }
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

        // Verify: WaitingForPayment state persisted
        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);
        var stateAfterInitiation = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterInitiation.Should().NotBeNull();
            stateAfterInitiation.CurrentState.Should().Be("WaitingForPayment");
            stateAfterInitiation.UserId.Should().Be(userId);
            stateAfterInitiation.PaymentMethodId.Should().Be(paymentMethodId);
            stateAfterInitiation.DurationDays.Should().Be(90);
            stateAfterInitiation.Amount.Should().Be(24.99m);
            stateAfterInitiation.Currency.Should().Be("USD");
            stateAfterInitiation.ExtensionInitiatedAtUtc.Should().NotBe(default);
        }

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

        // Verify: AwaitingExtension state persisted
        await SagaHarness.Exists(correlationId, x => x.AwaitingExtension, DefaultTimeout);
        var stateAfterPayment = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterPayment.Should().NotBeNull();
            stateAfterPayment.CurrentState.Should().Be("AwaitingExtension");
            stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
            stateAfterPayment.PaymentCompletedAtUtc.Should().HaveValue();
        }

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

        // Verify: ExtensionCompleted - saga finalized
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);

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

        // Assert
        var sagaFinalized = await SagaHarness.NotExists(correlationId, DefaultTimeout) is null;
        sagaFinalized.Should().BeTrue();
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when extension fails with ShouldCompensate=true");
            var outboxMessages = FakeOutboxWriter.GetMessages<RequestRefundCommand>().ToList();
            outboxMessages.Should().ContainSingle();
            outboxMessages[0].IntegrationEvent.CorrelationId.Should().Be(correlationId);
        }
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
        var compensationCompletedEvent = new AlertSubscriptionExtensionCompensationCompletedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            RefundTransactionId = refundTransactionId,
            CompensatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await TestHarness.Bus.Publish(compensationCompletedEvent);
        await SagaHarness.Consumed.Any<AlertSubscriptionExtensionCompensationCompletedSagaEvent>();

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

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
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

        // Assert
        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.Set<AlertSubscriptionExtensionSagaState>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            FakeOutboxWriter.HasMessage<RequestRefundCommand>().Should().BeTrue(
                "RequestRefundCommand should be added to the outbox when extension times out");
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
        var initiatedEvent = CreateExtensionInitiatedEvent(
            correlationId, userId, durationDays, amount, currency);
        await TestHarness.Bus.Publish(initiatedEvent);

        await SagaHarness.Exists(correlationId, x => x.WaitingForPayment, DefaultTimeout);

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

        await SagaHarness.Exists(correlationId, x => x.AwaitingExtension, DefaultTimeout);
    }

    private async Task TransitionSagaToCompensationInProgressState(
        Guid correlationId,
        Guid userId,
        Guid paymentTransactionId,
        int durationDays = 365,
        decimal amount = 99.99m,
        string currency = "USD")
    {
        await TransitionSagaToAwaitingExtensionState(
            correlationId, userId, paymentTransactionId, durationDays, amount, currency);

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

        await SagaHarness.Exists(correlationId, x => x.CompensationInProgress, DefaultTimeout);
    }
}
