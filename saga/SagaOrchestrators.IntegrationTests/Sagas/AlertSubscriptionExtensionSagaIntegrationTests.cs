using Finance.Payments;
using Microsoft.EntityFrameworkCore;
using Order.AlertSubscriptions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.IntegrationTests.Common;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Schedules;
using Weather.Alerts;

namespace SagaOrchestrators.IntegrationTests.Sagas;

[Collection(nameof(SagaTestCollection))]
public class AlertSubscriptionExtensionSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private SagaStateMonitor<AlertSubscriptionExtensionSagaOrchestrator, AlertSubscriptionExtensionSagaState> SagaStateMonitor
    {
        get;
    }

    public AlertSubscriptionExtensionSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor =
            CreateSagaStateMonitor<AlertSubscriptionExtensionSagaOrchestrator, AlertSubscriptionExtensionSagaState>();
    }

    [Fact]
    public async Task WhenExtensionInitiated_ShouldTransitionToAndPersistWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var extensionInitiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);

        // Act
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId, extensionInitiatedEvent);
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);

        // Assert
        var persistedState = await SagaDbContext.AlertSubscriptionExtensionSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be("WaitingForPayment");
            persistedState.DurationDays.Should().Be(30);
            outboxMessages.Should().ContainSingle();
            outboxMessages.Should().ContainSingle(om => om.Type == "Finance.Payments.PaymentRequestedEvent"
                                                        && om.KafkaKey == correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenSubscriptionExtended_ShouldTransitionToAndPersistExtensionCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var newExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(365).UtcDateTime;

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - transition to completed state via extension
        var extendedEvent = new AlertSubscriptionExtendedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 365,
            NewExpiresAtUtc = newExpiresAtUtc,
            ExtendedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.WeatherAlertSubscriptions, userId, extendedEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenMultipleSagasInitiated_ShouldMaintainIsolatedStates()
    {
        // Arrange
        var correlationId1 = Guid.CreateVersion7();
        var correlationId2 = Guid.CreateVersion7();
        var userId1 = Guid.CreateVersion7();
        var userId2 = Guid.CreateVersion7();

        var initiatedEvent1 = CreateExtensionInitiatedEvent(correlationId1, userId1, 30, 9.99m);
        var initiatedEvent2 = CreateExtensionInitiatedEvent(correlationId2, userId2, 365, 99.99m);

        // Act
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId1, initiatedEvent1);
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId2, initiatedEvent2);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId1, state => state.WaitingForPayment, DefaultTimeout);
        await SagaStateMonitor.WaitForStateAsync(correlationId2, state => state.WaitingForPayment, DefaultTimeout);

        var state1 = await SagaDbContext.AlertSubscriptionExtensionSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await SagaDbContext.AlertSubscriptionExtensionSagaStates
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
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var newExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(90).UtcDateTime;

        // Step 1: Initiate extension
        var initiatedEvent = CreateExtensionInitiatedEvent(
            correlationId, userId, 90, 24.99m, "USD", paymentMethodId);

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId, initiatedEvent);

        // Verify: WaitingForPayment state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);
        var stateAfterInitiation = await SagaDbContext.AlertSubscriptionExtensionSagaStates
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
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 24.99m.ToAvroDecimal(4),
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.FinancePayments, userId, paymentCompletedEvent);

        // Verify: AwaitingExtension state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingExtension, DefaultTimeout);
        var stateAfterPayment = await SagaDbContext.AlertSubscriptionExtensionSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessagesAfterPayment = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            stateAfterPayment.Should().NotBeNull();
            stateAfterPayment.CurrentState.Should().Be("AwaitingExtension");
            stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
            stateAfterPayment.PaymentCompletedAtUtc.Should().HaveValue();

            outboxMessagesAfterPayment.Should().Contain(om => om.Type == "Finance.Payments.PaymentRequestedEvent"
                                                              && om.KafkaKey == correlationId.ToString());
            outboxMessagesAfterPayment.Should().Contain(om => om.Type == "Weather.Alerts.ExtendAlertSubscriptionCommand"
                                                              && om.KafkaKey == correlationId.ToString());
        }

        // Step 3: Extend subscription
        var extendedEvent = new AlertSubscriptionExtendedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            DurationExtendedDays = 90,
            NewExpiresAtUtc = newExpiresAtUtc,
            ExtendedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.WeatherAlertSubscriptions, userId, extendedEvent);

        // Verify: ExtensionCompleted - saga finalized
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId, initiatedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);

        // Act - Send payment failed event via Kafka
        var paymentFailedEvent = new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "PAYMENT_DECLINED",
            ErrorMessage = "Payment was declined",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.FinancePayments, userId, paymentFailedEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenExtensionFailsWithCompensation_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Send extension failed with ShouldCompensate=true (internal saga event - no Kafka consumer)
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_FAILED",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await Bus.Publish(extensionFailedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
        var persistedState = await SagaDbContext.AlertSubscriptionExtensionSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            outboxMessages.Should().Contain(om => om.Type == "Finance.Payments.RequestRefundCommand"
                                                  && om.KafkaKey == correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenExtensionFailsWithoutCompensation_ShouldFinalizeInExtensionFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Send extension failed with ShouldCompensate=false (internal saga event - no Kafka consumer)
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_ALREADY_APPLIED",
            ErrorMessage = "Extension already applied for this period",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = false
        };

        await Bus.Publish(extensionFailedEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCompensationCompletes_ShouldFinalizeInCompensationCompletedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var refundTransactionId = Guid.CreateVersion7();

        await TransitionSagaToCompensationInProgressState(correlationId, userId, paymentTransactionId);

        // Act - Send compensation completed event via Kafka (PaymentRefundedEvent)
        var refundedEvent = new PaymentRefundedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = refundTransactionId,
            RefundedAmount = 99.99m.ToAvroDecimal(4),
            Currency = "USD",
            RefundedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.FinancePayments, userId, refundedEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentTimesOut_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var initiatedEvent = CreateExtensionInitiatedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId, initiatedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);

        // Act - Simulate timeout by publishing PaymentTimeoutExpired (MassTransit internal)
        var timeoutEvent = new PaymentTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenExtensionTimesOut_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingExtensionState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing ExtensionTimeoutExpired (MassTransit internal)
        var timeoutEvent = new ExtensionTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
        var persistedState = await SagaDbContext.AlertSubscriptionExtensionSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await SagaDbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.CurrentState.Should().Be("CompensationInProgress");
            persistedState.CompensationTriggered.Should().BeTrue();

            outboxMessages.Should().Contain(om => om.Type == "Finance.Payments.RequestRefundCommand"
                                                  && om.KafkaKey == correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenCompensationTimesOut_ShouldFinalizeInCompensationFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToCompensationInProgressState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing CompensationTimeoutExpired (MassTransit internal)
        var timeoutEvent = new CompensationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    // -- Helper Methods --

    private AlertSubscriptionExtensionInitiatedEvent CreateExtensionInitiatedEvent(
        Guid correlationId,
        Guid userId,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new AlertSubscriptionExtensionInitiatedEvent
        {
            AlertSubscriptionOrderId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.CreateVersion7(),
            DurationDays = durationDays,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
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
        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.OrderAlertSubscriptions, userId, initiatedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);

        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            TopicsOptions.FinancePayments, userId, paymentCompletedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingExtension, DefaultTimeout);
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

        // Extension failed is an internal saga event (no Kafka consumer)
        var extensionFailedEvent = new AlertSubscriptionExtensionFailedSagaEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "EXTENSION_FAILED",
            ErrorMessage = "Failed to extend subscription",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime,
            ShouldCompensate = true
        };

        await Bus.Publish(extensionFailedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
    }
}
