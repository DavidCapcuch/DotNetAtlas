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
    private readonly ILogger<AlertSubscriptionExtensionSaga> _logger;
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
        ILogger<AlertSubscriptionExtensionSaga> logger,
        IOptions<SagaOptions> sagaOptions,
        IOptions<SagaTopicsOptions> topicsOptions,
        TimeProvider timeProvider)
    {
        _logger = logger;
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
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
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
                .Then(InitializeSagaState)
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
                .Then(HandlePaymentCompleted)
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
                .Then(HandlePaymentFailed)
                .Activity(x => x.OfType<PaymentFailedActivity>())
                .Unschedule(PaymentTimeout)
                .TransitionTo(PaymentFailed)
                .Finalize(),
            When(PaymentTimeout.Received)
                .Then(HandlePaymentTimeout)
                .Activity(x => x.OfType<PaymentTimeoutActivity>())
                .TransitionTo(PaymentFailed)
                .Finalize());
    }

    private void ConfigureAwaitingExtensionState()
    {
        // Awaiting extension - can receive extended, failed, or timeout
        During(AwaitingExtension,
            When(SubscriptionExtendedEvent)
                .Then(HandleExtensionCompleted)
                .Activity(x => x.OfType<ExtensionCompletedActivity>())
                .Unschedule(ExtensionTimeout)
                .TransitionTo(ExtensionCompleted)
                .Finalize(),
            When(SubscriptionExtensionFailedEvent)
                .Then(HandleExtensionFailed)
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
                .Then(HandleExtensionTimeout)
                .Activity(x => x.OfType<ExtensionTimeoutActivity>())
                .Then(ctx => ctx.Saga.CompensationTriggered = true)
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
                .Then(HandleCompensationCompleted)
                .Activity(x => x.OfType<CompensationCompletedActivity>())
                .Unschedule(CompensationTimeout)
                .TransitionTo(CompensationCompleted)
                .Finalize(),
            When(CompensationTimeout.Received)
                .Then(HandleCompensationTimeout)
                .Activity(x => x.OfType<CompensationTimeoutActivity>())
                .TransitionTo(CompensationFailed)
                .Finalize());
    }

    private void InitializeSagaState(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent> ctx)
    {
        var message = ctx.Message;
        ctx.Saga.UserId = message.UserId;
        ctx.Saga.PaymentMethodId = message.PaymentMethodId;
        ctx.Saga.DurationDays = message.DurationDays;
        ctx.Saga.Amount = message.Amount;
        ctx.Saga.Currency = message.Currency;
        ctx.Saga.IdempotencyKey = message.IdempotencyKey;
        ctx.Saga.ExtensionInitiatedAtUtc = message.InitiatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} initialized for user {UserId}, duration {DurationDays} days",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.DurationDays);
    }

    private void HandleExtensionCompleted(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent> ctx)
    {
        ctx.Saga.ExtensionCompletedAtUtc = ctx.Message.ExtendedAtUtc;
        ctx.Saga.NewExpiresAtUtc = ctx.Message.NewExpiresAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} completed successfully for user {UserId}. New expiry: {NewExpiresAtUtc}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.NewExpiresAtUtc);
    }

    private void HandleExtensionFailed(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} extension failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleExtensionTimeout(BehaviorContext<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "EXTENSION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Extension timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} timed out waiting for extension response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandlePaymentCompleted(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent> ctx)
    {
        ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
        ctx.Saga.PaymentCompletedAtUtc = ctx.Message.CompletedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} payment completed for user {UserId}. TransactionId: {PaymentTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.PaymentTransactionId);
    }

    private void HandlePaymentFailed(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandlePaymentTimeout(
        BehaviorContext<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "PAYMENT_TIMEOUT";
        ctx.Saga.ErrorMessage = "Payment timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} timed out waiting for payment response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleCompensationCompleted(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionCompensationCompletedSagaEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.CompensatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.RefundTransactionId);
    }

    private void HandleCompensationTimeout(
        BehaviorContext<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "COMPENSATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Compensation did not complete in time";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Extension Saga {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }
}
