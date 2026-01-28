using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Observability.Activities;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Schedules;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga;

/// <summary>
/// MassTransit state machine implementing the subscription purchase saga.
/// Orchestrates the subscription activation flow with proper error handling and compensation.
/// </summary>
/// <remarks>
/// See README.md for the state diagram and detailed documentation.
/// </remarks>
public sealed class SubscriptionPurchaseSaga : MassTransitStateMachine<SubscriptionPurchaseSagaState>
{
    private readonly ILogger<SubscriptionPurchaseSaga> _logger;
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
    /// State indicating the saga is awaiting subscription activation.
    /// </summary>
    public State AwaitingActivation { get; private set; } = null!;

    /// <summary>
    /// State indicating activation has completed successfully.
    /// </summary>
    public State ActivationCompleted { get; private set; } = null!;

    /// <summary>
    /// State indicating activation has failed.
    /// </summary>
    public State ActivationFailed { get; private set; } = null!;

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
    /// Event triggered when a subscription purchase is initiated.
    /// </summary>
    public Event<SubscriptionPurchaseInitiatedEvent> SubscriptionPurchaseInitiated { get; private set; } = null!;

    /// <summary>
    /// Event triggered when payment is completed successfully.
    /// </summary>
    public Event<PaymentCompletedEvent> PaymentCompleted { get; private set; } = null!;

    /// <summary>
    /// Event triggered when payment fails.
    /// </summary>
    public Event<PaymentFailedEvent> PaymentFailed_ { get; private set; } = null!;

    /// <summary>
    /// Event triggered when subscription activation succeeds.
    /// </summary>
    public Event<SubscriptionActivatedEvent> SubscriptionActivated { get; private set; } = null!;

    /// <summary>
    /// Event triggered when subscription activation fails.
    /// </summary>
    public Event<SubscriptionActivationFailedEvent> SubscriptionActivationFailed { get; private set; } = null!;

    /// <summary>
    /// Event triggered when compensation is completed.
    /// </summary>
    public Event<SubscriptionCompensationCompletedEvent> CompensationCompletedEvent { get; private set; } = null!;

    /// <summary>
    /// Schedule for payment timeout handling.
    /// </summary>
    public Schedule<SubscriptionPurchaseSagaState, PaymentTimeoutExpired> PaymentTimeout { get; private set; } = null!;

    /// <summary>
    /// Schedule for activation timeout handling.
    /// </summary>
    public Schedule<SubscriptionPurchaseSagaState, ActivationTimeoutExpired> ActivationTimeout { get; private set; } =
        null!;

    /// <summary>
    /// Schedule for compensation timeout handling.
    /// </summary>
    public Schedule<SubscriptionPurchaseSagaState, CompensationTimeoutExpired>
        CompensationTimeout
    { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of <see cref="SubscriptionPurchaseSaga"/>.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="sagaOptions">The saga configuration options.</param>
    /// <param name="timeProvider">The time provider for testable time operations.</param>
    public SubscriptionPurchaseSaga(
        ILogger<SubscriptionPurchaseSaga> logger,
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
        Event(() => SubscriptionPurchaseInitiated, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new SubscriptionPurchaseSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
        });

        Event(() => PaymentCompleted, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => PaymentFailed_, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => SubscriptionActivated, e =>
            e.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => SubscriptionActivationFailed, e =>
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
        // Initial state - when purchase initiated event arrives, request payment
        Initially(
            When(SubscriptionPurchaseInitiated)
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
                .PublishAsync(ctx => ctx.Init<Weather.Alerts.ActivateSubscriptionCommand>(
                    new Weather.Alerts.ActivateSubscriptionCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Tier = MapToWeatherTier(ctx.Saga.SubscriptionTier),
                        DurationDays = ctx.Saga.DurationDays,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(ActivationTimeout,
                    ctx => new ActivationTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingActivation),
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

    private void ConfigureAwaitingActivationState()
    {
        // Awaiting activation - can receive activated, failed, or timeout
        During(AwaitingActivation,
            When(SubscriptionActivated)
                .Then(HandleActivationCompleted)
                .Activity(x => x.OfType<ActivationCompletedActivity>())
                .Unschedule(ActivationTimeout)
                .TransitionTo(ActivationCompleted)
                .Finalize(),
            When(SubscriptionActivationFailed)
                .Then(HandleActivationFailed)
                .Activity(x => x.OfType<ActivationFailedActivity>())
                .Unschedule(ActivationTimeout)
                .IfElse(ctx => ctx.Message.ShouldCompensate,
                    compensate => compensate
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishAsync(ctx => ctx.Init<Finance.Payments.RequestRefundCommand>(
                            new Finance.Payments.RequestRefundCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                                Reason = $"Activation failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
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
                .Then(HandleActivationTimeout)
                .Activity(x => x.OfType<ActivationTimeoutActivity>())
                .Then(ctx => ctx.Saga.CompensationTriggered = true)
                .PublishAsync(ctx => ctx.Init<Finance.Payments.RequestRefundCommand>(
                    new Finance.Payments.RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = "Activation timeout expired",
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

    private static Weather.Alerts.SubscriptionTier MapToWeatherTier(Order.AlertSubscriptions.SubscriptionTier tier)
    {
        return tier switch
        {
            Order.AlertSubscriptions.SubscriptionTier.Pro => Weather.Alerts.SubscriptionTier.Pro,
            Order.AlertSubscriptions.SubscriptionTier.Ultra => Weather.Alerts.SubscriptionTier.Ultra,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown subscription tier")
        };
    }

    private void InitializeSagaState(BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent> ctx)
    {
        var message = ctx.Message;
        ctx.Saga.UserId = message.UserId;
        ctx.Saga.PaymentMethodId = message.PaymentMethodId;
        ctx.Saga.SubscriptionTier = message.SubscriptionTier;
        ctx.Saga.DurationDays = message.DurationDays;
        ctx.Saga.Amount = message.Amount;
        ctx.Saga.Currency = message.Currency;
        ctx.Saga.IdempotencyKey = message.IdempotencyKey;
        ctx.Saga.PurchaseInitiatedAtUtc = message.InitiatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Saga {CorrelationId} initialized for user {UserId}, tier {Tier}, duration {DurationDays} days",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.SubscriptionTier, ctx.Saga.DurationDays);
    }

    private void HandleActivationCompleted(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent> ctx)
    {
        ctx.Saga.ActivationCompletedAtUtc = ctx.Message.ActivatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Saga {CorrelationId} completed successfully for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleActivationFailed(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Saga {CorrelationId} activation failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleActivationTimeout(BehaviorContext<SubscriptionPurchaseSagaState, ActivationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "ACTIVATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Activation timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Saga {CorrelationId} timed out waiting for activation response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandlePaymentCompleted(
        BehaviorContext<SubscriptionPurchaseSagaState, PaymentCompletedEvent> ctx)
    {
        ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
        ctx.Saga.PaymentCompletedAtUtc = ctx.Message.CompletedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Saga {CorrelationId} payment completed for user {UserId}. TransactionId: {PaymentTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.PaymentTransactionId);
    }

    private void HandlePaymentFailed(
        BehaviorContext<SubscriptionPurchaseSagaState, PaymentFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Saga {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandlePaymentTimeout(
        BehaviorContext<SubscriptionPurchaseSagaState, PaymentTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "PAYMENT_TIMEOUT";
        ctx.Saga.ErrorMessage = "Payment timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Saga {CorrelationId} timed out waiting for payment response for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleCompensationCompleted(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionCompensationCompletedEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.CompensatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Saga {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.RefundTransactionId);
    }

    private void HandleCompensationTimeout(
        BehaviorContext<SubscriptionPurchaseSagaState, CompensationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "COMPENSATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Compensation did not complete in time";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Saga {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }
}
