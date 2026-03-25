using MassTransit;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Extensions;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Schedules;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga;

/// <summary>
/// MassTransit state machine implementing the subscription extension saga.
/// Orchestrates the subscription extension flow with proper error handling and compensation.
/// </summary>
public sealed class
    AlertSubscriptionExtensionSagaOrchestrator : MassTransitStateMachine<AlertSubscriptionExtensionSagaState>
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
    public Event<AlertSubscriptionExtensionInitiatedSagaEvent> AlertSubscriptionExtensionInitiatedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionPaymentCompletedSagaEvent> PaymentCompletedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionPaymentFailedSagaEvent> PaymentFailedEvent { get; private set; }
    public Event<AlertSubscriptionExtendedSagaEvent> AlertSubscriptionExtendedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionFailedSagaEvent> AlertSubscriptionExtensionFailedEvent { get; private set; }
    public Event<AlertSubscriptionExtensionCompensationCompletedSagaEvent> CompensationCompletedEvent
    {
        get;
        private set;
    }

    // Schedules
    public Schedule<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; }
    public Schedule<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> ExtensionTimeout { get; private set; }

    public Schedule<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> CompensationTimeout
    {
        get;
        private set;
    }

    public AlertSubscriptionExtensionSagaOrchestrator(
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
            When(AlertSubscriptionExtensionInitiatedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.UserId = ctx.Message.UserId;
                    ctx.Saga.PaymentMethodId = ctx.Message.PaymentMethodId;
                    ctx.Saga.DurationDays = ctx.Message.DurationDays;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.Currency = ctx.Message.Currency;
                    ctx.Saga.ExtensionInitiatedAtUtc = ctx.Message.InitiatedAtUtc;
                })
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
                        IdempotencyKey = ctx.Saga.CorrelationId.ToString(),
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
                .PublishToOutbox(
                    _topicsOptions.OrderAlertSubscriptions,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AlertSubscriptionExtensionFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = ctx.Saga.ErrorCode ?? "PAYMENT_FAILED",
                        ErrorMessage = ctx.Saga.ErrorMessage ?? "Payment failed",
                        CompensationTriggered = false,
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .TransitionTo(PaymentFailed)
                .Finalize(),
            When(PaymentTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "PAYMENT_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Payment timeout expired";
                })
                .Activity(x => x.OfType<PaymentTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.OrderAlertSubscriptions,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AlertSubscriptionExtensionFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = "PAYMENT_TIMEOUT",
                        ErrorMessage = "Payment timeout expired",
                        CompensationTriggered = false,
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .TransitionTo(PaymentFailed)
                .Finalize());
    }

    private void ConfigureAwaitingExtensionState()
    {
        // Awaiting extension - can receive extended, failed, or timeout
        During(AwaitingExtension,
            When(AlertSubscriptionExtendedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.ExtensionCompletedAtUtc = ctx.Message.ExtendedAtUtc;
                    ctx.Saga.NewExpiresAtUtc = ctx.Message.NewExpiresAtUtc;
                })
                .Activity(x => x.OfType<ExtensionCompletedActivity>())
                .Unschedule(ExtensionTimeout)
                .PublishToOutbox(
                    _topicsOptions.OrderAlertSubscriptions,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AlertSubscriptionExtensionCompletedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .TransitionTo(ExtensionCompleted)
                .Finalize(),
            When(AlertSubscriptionExtensionFailedEvent)
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
                        .PublishToOutbox(
                            _topicsOptions.OrderAlertSubscriptions,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new AlertSubscriptionExtensionFailedEvent
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                ErrorCode = ctx.Saga.ErrorCode ?? "EXTENSION_FAILED",
                                ErrorMessage = ctx.Saga.ErrorMessage ?? "Extension failed",
                                CompensationTriggered = false,
                                FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
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
                .PublishToOutbox(
                    _topicsOptions.OrderAlertSubscriptions,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AlertSubscriptionExtensionFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = ctx.Saga.ErrorCode ?? "EXTENSION_FAILED",
                        ErrorMessage = ctx.Saga.ErrorMessage ?? "Extension failed, compensation completed",
                        CompensationTriggered = true,
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .TransitionTo(CompensationCompleted)
                .Finalize(),
            When(CompensationTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "COMPENSATION_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Compensation did not complete in time";
                })
                .Activity(x => x.OfType<CompensationTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.OrderAlertSubscriptions,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AlertSubscriptionExtensionFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = "COMPENSATION_TIMEOUT",
                        ErrorMessage = "Compensation did not complete in time",
                        CompensationTriggered = true,
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .TransitionTo(CompensationFailed)
                .Finalize());
    }

    private void ConfigureEvents()
    {
        Event(() => AlertSubscriptionExtensionInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        // Intermediate events - missing saga indicates a bug (event arrived for non-existent saga)
        Event(() => PaymentCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => AlertSubscriptionExtendedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => AlertSubscriptionExtensionFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
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
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.SubscriptionTimeouts.ExtensionMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => CompensationTimeout, instance => instance.CompensationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.SubscriptionTimeouts.CompensationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });
    }
}
