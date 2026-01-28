using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability.Activities;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Schedules;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga;

/// <summary>
/// MassTransit state machine implementing the subscription extension saga.
/// Orchestrates the subscription extension flow with proper error handling and compensation.
/// </summary>
public sealed class SubscriptionExtensionSaga : MassTransitStateMachine<SubscriptionExtensionSagaState>
{
    private readonly ILogger<SubscriptionExtensionSaga> _logger;
    private readonly SagaOptions _sagaOptions;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// State indicating the saga is waiting for payment to complete.
    /// </summary>
    public State WaitingForPayment { get; private set; } = null!;

    /// <summary>
    /// State indicating payment has failed.
    /// </summary>
    public State PaymentFailed { get; private set; } = null!;

    /// <summary>
    /// State indicating the saga is awaiting subscription extension.
    /// </summary>
    public State AwaitingExtension { get; private set; } = null!;

    /// <summary>
    /// State indicating extension has completed successfully.
    /// </summary>
    public State ExtensionCompleted { get; private set; } = null!;

    /// <summary>
    /// State indicating extension has failed.
    /// </summary>
    public State ExtensionFailed { get; private set; } = null!;

    /// <summary>
    /// State indicating compensation is in progress.
    /// </summary>
    public State CompensationInProgress { get; private set; } = null!;

    /// <summary>
    /// State indicating compensation has completed.
    /// </summary>
    public State CompensationCompleted { get; private set; } = null!;

    /// <summary>
    /// State indicating compensation has failed (timed out).
    /// </summary>
    public State CompensationFailed { get; private set; } = null!;

    /// <summary>
    /// Event triggered when a subscription extension is initiated.
    /// </summary>
    public Event<SubscriptionExtensionInitiatedEvent> SubscriptionExtensionInitiated { get; private set; } = null!;

    /// <summary>
    /// Event triggered when payment is completed successfully.
    /// </summary>
    public Event<PaymentCompletedEvent> PaymentCompleted { get; private set; } = null!;

    /// <summary>
    /// Event triggered when payment fails.
    /// </summary>
    public Event<PaymentFailedEvent> PaymentFailed_ { get; private set; } = null!;

    /// <summary>
    /// Event triggered when subscription extension succeeds.
    /// </summary>
    public Event<SubscriptionExtendedEvent> SubscriptionExtended { get; private set; } = null!;

    /// <summary>
    /// Event triggered when subscription extension fails.
    /// </summary>
    public Event<SubscriptionExtensionFailedEvent> SubscriptionExtensionFailed { get; private set; } = null!;

    /// <summary>
    /// Event triggered when compensation is completed.
    /// </summary>
    public Event<ExtensionCompensationCompletedEvent> CompensationCompletedEvent { get; private set; } = null!;

    /// <summary>
    /// Schedule for payment timeout handling.
    /// </summary>
    public Schedule<SubscriptionExtensionSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; } = null!;

    /// <summary>
    /// Schedule for extension timeout handling.
    /// </summary>
    public Schedule<SubscriptionExtensionSagaState, ExtensionTimeoutExpired> ExtensionTimeout { get; private set; } =
        null!;

    /// <summary>
    /// Schedule for compensation timeout handling.
    /// </summary>
    public Schedule<SubscriptionExtensionSagaState, CompensationTimeoutExpired>
        CompensationTimeout
    { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of <see cref="SubscriptionExtensionSaga"/>.
    /// </summary>
    public SubscriptionExtensionSaga(
        ILogger<SubscriptionExtensionSaga> logger,
        IOptions<SagaOptions> sagaOptions,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _sagaOptions = sagaOptions.Value;
        _timeProvider = timeProvider;

        ConfigureEvents();
        ConfigureSchedules();
        ConfigureStateMachine();
    }

    private void ConfigureEvents()
    {
        Event(() => SubscriptionExtensionInitiated, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new SubscriptionExtensionSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
        });

        Event(() => PaymentCompleted, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => PaymentFailed_, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => SubscriptionExtended, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => SubscriptionExtensionFailed, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => CompensationCompletedEvent, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));
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
            When(SubscriptionExtensionInitiated)
                .Then(InitializeSagaState)
                .Activity(x => x.OfType<SagaStartedActivity>())
                .PublishAsync(ctx => ctx.Init<Finance.Payments.PaymentRequestedEvent>(
                    new Finance.Payments.PaymentRequestedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentMethodId = ctx.Saga.PaymentMethodId,
                        Amount = new Avro.AvroDecimal(ctx.Saga.Amount),
                        Currency = ctx.Saga.Currency,
                        IdempotencyKey = ctx.Saga.IdempotencyKey,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
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
            When(PaymentCompleted)
                .Then(HandlePaymentCompleted)
                .Activity(x => x.OfType<PaymentCompletedActivity>())
                .Unschedule(PaymentTimeout)
                .PublishAsync(ctx => ctx.Init<Weather.Alerts.ExtendSubscriptionCommand>(
                    new Weather.Alerts.ExtendSubscriptionCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        DurationDays = ctx.Saga.DurationDays,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(ExtensionTimeout,
                    ctx => new ExtensionTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingExtension),
            When(PaymentFailed_)
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
            When(SubscriptionExtended)
                .Then(HandleExtensionCompleted)
                .Activity(x => x.OfType<ExtensionCompletedActivity>())
                .Unschedule(ExtensionTimeout)
                .TransitionTo(ExtensionCompleted)
                .Finalize(),
            When(SubscriptionExtensionFailed)
                .Then(HandleExtensionFailed)
                .Activity(x => x.OfType<ExtensionFailedActivity>())
                .Unschedule(ExtensionTimeout)
                .IfElse(ctx => ctx.Message.ShouldCompensate,
                    compensate => compensate
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishAsync(ctx => ctx.Init<Finance.Payments.RequestRefundCommand>(
                            new Finance.Payments.RequestRefundCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                                Reason = $"Extension failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
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
                .PublishAsync(ctx => ctx.Init<Finance.Payments.RequestRefundCommand>(
                    new Finance.Payments.RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = "Extension timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedEvent> ctx)
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtendedEvent> ctx)
    {
        ctx.Saga.ExtensionCompletedAtUtc = ctx.Message.ExtendedAtUtc;
        ctx.Saga.NewExpiresAtUtc = ctx.Message.NewExpiresAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} completed successfully for user {UserId}. New expiry: {NewExpiresAtUtc}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.NewExpiresAtUtc);
    }

    private void HandleExtensionFailed(
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} extension failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleExtensionTimeout(BehaviorContext<SubscriptionExtensionSagaState, ExtensionTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "EXTENSION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Extension timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} timed out waiting for extension response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandlePaymentCompleted(
        BehaviorContext<SubscriptionExtensionSagaState, PaymentCompletedEvent> ctx)
    {
        ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
        ctx.Saga.PaymentCompletedAtUtc = ctx.Message.CompletedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} payment completed for user {UserId}. TransactionId: {PaymentTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.PaymentTransactionId);
    }

    private void HandlePaymentFailed(
        BehaviorContext<SubscriptionExtensionSagaState, PaymentFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandlePaymentTimeout(
        BehaviorContext<SubscriptionExtensionSagaState, PaymentTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "PAYMENT_TIMEOUT";
        ctx.Saga.ErrorMessage = "Payment timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Extension Saga {CorrelationId} timed out waiting for payment response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleCompensationCompleted(
        BehaviorContext<SubscriptionExtensionSagaState, ExtensionCompensationCompletedEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.CompensatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Extension Saga {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.RefundTransactionId);
    }

    private void HandleCompensationTimeout(
        BehaviorContext<SubscriptionExtensionSagaState, CompensationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "COMPENSATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Compensation did not complete in time";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Extension Saga {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }
}
