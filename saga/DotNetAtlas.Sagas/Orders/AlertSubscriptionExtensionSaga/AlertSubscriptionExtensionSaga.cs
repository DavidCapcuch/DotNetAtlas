using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Extensions;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga;

/// <summary>
/// MassTransit state machine implementing the subscription extension saga.
/// Orchestrates the subscription extension flow with proper error handling and compensation.
/// </summary>
public sealed class AlertSubscriptionExtensionSaga : MassTransitStateMachine<AlertSubscriptionExtensionSagaState>
{
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State WaitingForPayment { get; private set; }
    public State PaymentFailed { get; private set; }
    public State AwaitingExtension { get; private set; }
    public State ExtensionCompleted { get; private set; }
    public State ExtensionFailed { get; private set; }
    public State CompensationInProgress { get; private set; }
    public State CompensationCompleted { get; private set; }
    public State CompensationFailed { get; private set; }

    // Events
    public Event<AlertSubscriptionExtensionInitiatedSagaEvent> SubscriptionExtensionInitiatedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionPaymentCompletedSagaEvent> PaymentCompletedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionPaymentFailedSagaEvent> PaymentFailedEvent { get; private set; }
    public Event<AlertSubscriptionExtendedSagaEvent> SubscriptionExtendedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionFailedSagaEvent> SubscriptionExtensionFailedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionCompensationCompletedSagaEvent> CompensationCompletedEvent { get; private set; }

    // Schedules
    public Schedule<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; }
    public Schedule<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> ExtensionTimeout { get; private set; }
    public Schedule<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> CompensationTimeout { get; private set; }

    public AlertSubscriptionExtensionSaga(
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
        Event(() => SubscriptionExtensionInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new AlertSubscriptionExtensionSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                UserId = ctx.Message.UserId,
                PaymentMethodId = ctx.Message.PaymentMethodId,
                DurationDays = ctx.Message.DurationDays,
                Amount = ctx.Message.Amount,
                Currency = ctx.Message.Currency,
                IdempotencyKey = ctx.Message.IdempotencyKey,
                ExtensionInitiatedAtUtc = ctx.Message.InitiatedAtUtc
            });
        });

        // Intermediate events - missing saga indicates a bug (event arrived for non-existent saga)
        Event(() => PaymentCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => SubscriptionExtendedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => SubscriptionExtensionFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        // Compensation completed - can legitimately arrive after saga finalized
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

        Schedule(() => ExtensionTimeout, instance => instance.ExtensionTimeoutTokenId, s =>
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
        ConfigureAwaitingExtensionState();
        ConfigureCompensationInProgressState();

        SetCompletedWhenFinalized();
    }

    private void ConfigureInitialState()
    {
        Initially(
            When(SubscriptionExtensionInitiatedEvent)
                .Activity(x => x.OfType<SagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.FinancePayments,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new global::Finance.Payments.PaymentRequestedEvent
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
                    ctx.Saga.PaymentCompletedAtUtc = ctx.Message.CompletedAtUtc;
                })
                .Activity(x => x.OfType<PaymentCompletedActivity>())
                .Unschedule(PaymentTimeout)
                .PublishToOutbox(
                    _topicsOptions.WeatherAlertSubscriptionsCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new Weather.Alerts.ExtendAlertSubscriptionCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        DurationDays = ctx.Saga.DurationDays,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(ExtensionTimeout,
                    ctx => new ExtensionTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingExtension),
            When(PaymentFailedEvent)
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

    private void ConfigureAwaitingExtensionState()
    {
        // Awaiting extension - can receive extended, failed, or timeout
        During(AwaitingExtension,
            When(SubscriptionExtendedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ExtensionCompletedAtUtc = ctx.Message.ExtendedAtUtc;
                    ctx.Saga.NewExpiresAtUtc = ctx.Message.NewExpiresAtUtc;
                })
                .Activity(x => x.OfType<ExtensionCompletedActivity>())
                .Unschedule(ExtensionTimeout)
                .TransitionTo(ExtensionCompleted)
                .Finalize(),
            When(SubscriptionExtensionFailedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
                    ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
                })
                .Activity(x => x.OfType<ExtensionFailedActivity>())
                .Unschedule(ExtensionTimeout)
                .IfElse(ctx => ctx.Message.ShouldCompensate,
                    compensate => compensate
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishToOutbox(
                            _topicsOptions.FinancePaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new global::Finance.Payments.RequestRefundCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                                Reason = $"Extension failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
                        .Schedule(CompensationTimeout,
                            ctx => new CompensationTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            })
                        .TransitionTo(CompensationInProgress),
                    noCompensate => noCompensate
                        .TransitionTo(ExtensionFailed)
                        .Finalize()),
            When(ExtensionTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "EXTENSION_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Extension timeout expired";
                    ctx.Saga.CompensationTriggered = true;
                })
                .Activity(x => x.OfType<ExtensionTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new global::Finance.Payments.RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = "Extension timeout expired",
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
                    ctx.Saga.CompensationCompletedAtUtc = ctx.Message.CompensatedAtUtc;
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
}
