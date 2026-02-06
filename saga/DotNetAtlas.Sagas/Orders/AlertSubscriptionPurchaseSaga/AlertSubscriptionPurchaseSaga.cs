using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Extensions;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using Finance.Payments;
using MassTransit;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga;

/// <summary>
/// MassTransit state machine implementing the subscription purchase saga.
/// Orchestrates the subscription activation flow with proper error handling and compensation.
/// </summary>
/// <remarks>
/// See README.md for the state diagram and detailed documentation.
/// </remarks>
public sealed class AlertSubscriptionPurchaseSaga : MassTransitStateMachine<AlertSubscriptionPurchaseSagaState>
{
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State WaitingForPayment { get; private set; }
    public State PaymentFailed { get; private set; }
    public State AwaitingActivation { get; private set; }
    public State ActivationCompleted { get; private set; }
    public State ActivationFailed { get; private set; }
    public State CompensationInProgress { get; private set; }
    public State CompensationCompleted { get; private set; }
    public State CompensationFailed { get; private set; }

    // Events
    public Event<AlertSubscriptionPurchaseInitiatedSagaEvent> AlertSubscriptionPurchaseInitiatedEvent { get; private set; }
    public Event<AlertSubscriptionPurchasePaymentCompletedSagaEvent> PaymentCompletedEvent { get; private set; }
    public Event<AlertSubscriptionPurchasePaymentFailedSagaEvent> PaymentFailedEventEvent { get; private set; }
    public Event<AlertSubscriptionActivatedSagaEvent> AlertSubscriptionActivatedEvent { get; private set; }
    public Event<AlertSubscriptionActivationFailedSagaEvent> AlertSubscriptionActivationFailedEvent { get; private set; }
    public Event<AlertSubscriptionPurchaseCompensationCompletedSagaEvent> CompensationCompletedEvent { get; private set; }

    // Schedules
    public Schedule<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; }

    public Schedule<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> ActivationTimeout { get; private set; }

    public Schedule<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired> CompensationTimeout { get; private set; }

    public AlertSubscriptionPurchaseSaga(
        IOptions<SagaOptions> sagaOptions,
        IOptions<SagaTopicsOptions> topicsOptions,
        TimeProvider timeProvider)
    {
        _sagaOptions = sagaOptions.Value;
        _topicsOptions = topicsOptions.Value;
        _timeProvider = timeProvider;

        ConfigureEvents();
        ConfigureSchedules();
        ConfigureStateMachine();
    }

    private void ConfigureEvents()
    {
        Event(() => AlertSubscriptionPurchaseInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new AlertSubscriptionPurchaseSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                UserId = ctx.Message.UserId,
                PaymentMethodId = ctx.Message.PaymentMethodId,
                SubscriptionTier = ctx.Message.SubscriptionTier,
                DurationDays = ctx.Message.DurationDays,
                Amount = ctx.Message.Amount,
                Currency = ctx.Message.Currency,
                IdempotencyKey = ctx.Message.IdempotencyKey,
                PurchaseInitiatedUtc = ctx.Message.InitiatedAtUtc
            });
        });

        // Intermediate events - missing saga indicates a bug (event arrived for non-existent saga)
        Event(() => PaymentCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentFailedEventEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => AlertSubscriptionActivatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => AlertSubscriptionActivationFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        // Compensation completed - can arrive after saga finalized
        Event(() => CompensationCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });
    }

    private void ConfigureSchedules()
    {
        Schedule(() => PaymentTimeout, instance => instance.PaymentTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.SubscriptionTimeouts.PaymentMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => ActivationTimeout, instance => instance.ActivationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.SubscriptionTimeouts.ActivationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => CompensationTimeout, instance => instance.CompensationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.SubscriptionTimeouts.CompensationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });
    }

    private void ConfigureStateMachine()
    {
        InstanceState(x => x.CurrentState);

        ConfigureInitialState();
        ConfigureWaitingForPaymentState();
        ConfigureAwaitingActivationState();
        ConfigureCompensationInProgressState();

        SetCompletedWhenFinalized();
    }

    private void ConfigureInitialState()
    {
        // Initial state - when purchase initiated event arrives, then publish payment request
        Initially(
            When(AlertSubscriptionPurchaseInitiatedEvent)
                .Activity(x => x.OfType<AlertSubscriptionPurchaseSagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.FinancePayments,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new PaymentRequestedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentMethodId = ctx.Saga.PaymentMethodId,
                        Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                        Currency = ctx.Saga.Currency,
                        IdempotencyKey = ctx.Saga.IdempotencyKey,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(PaymentTimeout,
                    ctx => new PaymentTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(WaitingForPayment));
    }

    private void ConfigureWaitingForPaymentState()
    {
        // Waiting for payment - can receive payment completed, failed, or timeout
        During(WaitingForPayment,
            When(PaymentCompletedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
                    ctx.Saga.PaymentCompletedUtc = ctx.Message.CompletedAtUtc;
                })
                .Activity(x => x.OfType<PaymentCompletedActivity>())
                .Unschedule(PaymentTimeout)
                .PublishToOutbox(
                    _topicsOptions.WeatherAlertSubscriptionsCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new ActivateAlertSubscriptionCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Tier = MapToWeatherTier(ctx.Saga.SubscriptionTier),
                        DurationDays = ctx.Saga.DurationDays,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(ActivationTimeout,
                    ctx => new ActivationTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingActivation),
            When(PaymentFailedEventEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
                    ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
                })
                .Activity(x => x.OfType<PaymentFailedActivity>())
                .Unschedule(PaymentTimeout)
                .TransitionTo(PaymentFailed)
                .Finalize(),
            When(PaymentTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "PAYMENT_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Payment timeout expired";
                })
                .Activity(x => x.OfType<PaymentTimeoutActivity>())
                .TransitionTo(PaymentFailed)
                .Finalize());
    }

    private void ConfigureAwaitingActivationState()
    {
        // Awaiting activation - can receive activated, failed, or timeout
        During(AwaitingActivation,
            When(AlertSubscriptionActivatedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ActivationCompletedUtc = ctx.Message.ActivatedAtUtc;
                })
                .Activity(x => x.OfType<ActivationCompletedActivity>())
                .Unschedule(ActivationTimeout)
                .TransitionTo(ActivationCompleted)
                .Finalize(),
            When(AlertSubscriptionActivationFailedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
                    ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
                })
                .Activity(x => x.OfType<ActivationFailedActivity>())
                .Unschedule(ActivationTimeout)
                .IfElse(ctx => ctx.Message.ShouldCompensate,
                    compensate => compensate
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishToOutbox(
                            _topicsOptions.FinancePaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new RequestRefundCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                                Reason = $"Activation failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
                        .Schedule(CompensationTimeout,
                            ctx => new CompensationTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            })
                        .TransitionTo(CompensationInProgress),
                    noCompensate => noCompensate
                        .TransitionTo(ActivationFailed)
                        .Finalize()),
            When(ActivationTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "ACTIVATION_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Activation timeout expired";
                    ctx.Saga.CompensationTriggered = true;
                })
                .Activity(x => x.OfType<ActivationTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = "Activation timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(CompensationTimeout,
                    ctx => new CompensationTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(CompensationInProgress));
    }

    private void ConfigureCompensationInProgressState()
    {
        // Compensation in progress - waiting for refund confirmation or timeout
        During(CompensationInProgress,
            When(CompensationCompletedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CompensationCompletedUtc = ctx.Message.CompensatedAtUtc;
                })
                .Activity(x => x.OfType<CompensationCompletedActivity>())
                .Unschedule(CompensationTimeout)
                .TransitionTo(CompensationCompleted)
                .Finalize(),
            When(CompensationTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "COMPENSATION_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Compensation did not complete in time";
                })
                .Activity(x => x.OfType<CompensationTimeoutActivity>())
                .TransitionTo(CompensationFailed)
                .Finalize());
    }

    private static SubscriptionTier MapToWeatherTier(Order.AlertSubscriptions.SubscriptionTier tier)
    {
        return tier switch
        {
            Order.AlertSubscriptions.SubscriptionTier.Pro => SubscriptionTier.Pro,
            Order.AlertSubscriptions.SubscriptionTier.Ultra => SubscriptionTier.Ultra,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown subscription tier")
        };
    }
}
