using DotNetAtlas.Sagas.IntegrationTests.Common;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using Finance.Payments;
using Microsoft.EntityFrameworkCore;
using Order.AlertSubscriptions;
using Weather.Alerts;
using SubscriptionTier = Order.AlertSubscriptions.SubscriptionTier;

namespace DotNetAtlas.Sagas.IntegrationTests.Sagas;

[Collection(nameof(SagaTestCollection))]
public class AlertSubscriptionPurchaseSagaIntegrationTests : BaseSagaIntegrationTest
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private SagaStateMonitor<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState> SagaStateMonitor
    {
        get;
    }

    public AlertSubscriptionPurchaseSagaIntegrationTests(SagaIntegrationTestFixture fixture)
        : base(fixture)
    {
        SagaStateMonitor = CreateSagaStateMonitor<AlertSubscriptionPurchaseSaga, AlertSubscriptionPurchaseSagaState>();
    }

    [Fact]
    public async Task WhenPurchaseInitiated_ShouldTransitionToAndPersistWaitingForPayment()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);

        // Act
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId, initiatedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);
        var persistedState = await DbContext.AlertSubscriptionPurchaseSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await DbContext.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        using (new AssertionScope())
        {
            persistedState.Should().NotBeNull();
            persistedState.UserId.Should().Be(userId);
            persistedState.CurrentState.Should().Be("WaitingForPayment");
            persistedState.SubscriptionTier.Should().Be(SubscriptionTier.Pro);
            persistedState.DurationDays.Should().Be(30);
            outboxMessages.Should().ContainSingle();
            outboxMessages.Should().ContainSingle(om => om.Type == "Finance.Payments.PaymentRequestedEvent"
                                                        && om.KafkaKey == correlationId.ToString());
        }
    }

    [Fact]
    public async Task WhenSubscriptionActivated_ShouldTransitionToAndPersistActivationCompleted()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - transition to completed via activation
        var activatedEvent = new AlertSubscriptionActivatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Tier = Weather.Alerts.SubscriptionTier.Ultra,
            DurationDays = 365,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(365).UtcDateTime,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.WeatherAlertSubscriptionsTopic, userId, activatedEvent);

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

        var initiatedEvent1 = CreatePurchaseInitiatedEvent(
            correlationId1, userId1, SubscriptionTier.Pro, 30, 9.99m);
        var initiatedEvent2 = CreatePurchaseInitiatedEvent(
            correlationId2, userId2, SubscriptionTier.Ultra, 365, 99.99m);

        // Act
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId1, initiatedEvent1);
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId2, initiatedEvent2);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId1, state => state.WaitingForPayment, DefaultTimeout);
        await SagaStateMonitor.WaitForStateAsync(correlationId2, state => state.WaitingForPayment, DefaultTimeout);

        var state1 = await DbContext.AlertSubscriptionPurchaseSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId1);

        var state2 = await DbContext.AlertSubscriptionPurchaseSagaStates
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
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentMethodId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        // Step 1: Initiate purchase
        var initiatedEvent = CreatePurchaseInitiatedEvent(
            correlationId, userId, SubscriptionTier.Pro, 30, 29.99m, "USD", paymentMethodId);

        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId, initiatedEvent);

        // Verify: WaitingForPayment state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);
        var stateAfterInitiation = await DbContext.AlertSubscriptionPurchaseSagaStates
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
            stateAfterInitiation.PurchaseInitiatedUtc.Should().NotBe(default);
        }

        // Step 2: Complete payment
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Amount = 29.99m.ToAvroDecimal(4),
            Currency = "USD",
            CompletedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.FinancePaymentsTopic, userId, paymentCompletedEvent);

        // Verify: AwaitingActivation state persisted
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingActivation, DefaultTimeout);
        var stateAfterPayment = await DbContext.AlertSubscriptionPurchaseSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        using (new AssertionScope())
        {
            stateAfterPayment.Should().NotBeNull();
            stateAfterPayment.CurrentState.Should().Be("AwaitingActivation");
            stateAfterPayment.PaymentTransactionId.Should().Be(paymentTransactionId);
            stateAfterPayment.PaymentCompletedUtc.Should().HaveValue();
        }

        // Step 3: Activate subscription
        var activatedEvent = new AlertSubscriptionActivatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentTransactionId = paymentTransactionId,
            Tier = Weather.Alerts.SubscriptionTier.Pro,
            DurationDays = 30,
            ExpiresAtUtc = TimeProvider.GetUtcNow().AddDays(30).UtcDateTime,
            ActivatedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.WeatherAlertSubscriptionsTopic, userId, activatedEvent);

        // Verify: ActivationCompleted - saga finalized
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenPaymentFails_ShouldFinalizeInPaymentFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId, initiatedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.WaitingForPayment, DefaultTimeout);

        // Act
        var paymentFailedEvent = new PaymentFailedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            ErrorCode = "PAYMENT_DECLINED",
            ErrorMessage = "Payment was declined",
            FailedAtUtc = TimeProvider.GetUtcNow().UtcDateTime
        };

        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.FinancePaymentsTopic, userId, paymentFailedEvent);

        // Assert
        var sagaFinalized = await SagaStateMonitor.WaitForFinalizedAsync(correlationId, DefaultTimeout);
        sagaFinalized.Should().BeTrue();
    }

    [Fact]
    public async Task WhenActivationFailsWithCompensation_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Send activation failed with ShouldCompensate=true (internal saga event - no Kafka consumer)
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

        await Bus.Publish(activationFailedEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.AlertSubscriptionPurchaseSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await DbContext.OutboxMessages
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
    public async Task WhenActivationFailsWithoutCompensation_ShouldFinalizeInActivationFailedState()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Send activation failed with ShouldCompensate=false (internal saga event - no Kafka consumer)
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

        await Bus.Publish(activationFailedEvent);

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
            SagaIntegrationTestFixture.FinancePaymentsTopic, userId, refundedEvent);

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

        var initiatedEvent = CreatePurchaseInitiatedEvent(correlationId, userId);
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId, initiatedEvent);

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
    public async Task WhenActivationTimesOut_ShouldTriggerRefundAndTransitionToCompensationInProgress()
    {
        // Arrange
        var correlationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();

        await TransitionSagaToAwaitingActivationState(correlationId, userId, paymentTransactionId);

        // Act - Simulate timeout by publishing ActivationTimeoutExpired (MassTransit internal)
        var timeoutEvent = new ActivationTimeoutExpired
        {
            CorrelationId = correlationId
        };

        await Bus.Publish(timeoutEvent);

        // Assert
        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
        var persistedState = await DbContext.AlertSubscriptionPurchaseSagaStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId);

        var outboxMessages = await DbContext.OutboxMessages
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

    private AlertSubscriptionPurchaseInitiatedEvent CreatePurchaseInitiatedEvent(
        Guid correlationId,
        Guid userId,
        SubscriptionTier tier = SubscriptionTier.Pro,
        int durationDays = 30,
        decimal amount = 9.99m,
        string currency = "USD",
        Guid? paymentMethodId = null)
    {
        return new AlertSubscriptionPurchaseInitiatedEvent
        {
            CorrelationId = correlationId,
            UserId = userId,
            PaymentMethodId = paymentMethodId ?? Guid.CreateVersion7(),
            Tier = tier,
            DurationDays = durationDays,
            Amount = amount.ToAvroDecimal(4),
            Currency = currency,
            IdempotencyKey = $"purchase-{userId}-{Guid.CreateVersion7()}",
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
        await KafkaTestProducer.ProduceAsync(
            SagaIntegrationTestFixture.OrderAlertSubscriptionsTopic, userId, initiatedEvent);

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
            SagaIntegrationTestFixture.FinancePaymentsTopic, userId, paymentCompletedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.AwaitingActivation, DefaultTimeout);
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

        // Activation failed is an internal saga event (no Kafka consumer)
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

        await Bus.Publish(activationFailedEvent);

        await SagaStateMonitor.WaitForStateAsync(correlationId, state => state.CompensationInProgress, DefaultTimeout);
    }
}
