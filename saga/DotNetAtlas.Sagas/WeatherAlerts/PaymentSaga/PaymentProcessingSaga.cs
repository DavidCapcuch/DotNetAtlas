using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Commands;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Schedules;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga;

/// <summary>
/// MassTransit state machine implementing the payment processing saga.
/// Orchestrates the complete payment lifecycle: initiation, authorization, capture,
/// activation, and compensation (void/refund).
/// </summary>
public sealed class PaymentProcessingSaga : MassTransitStateMachine<PaymentSagaState>
{
    private readonly ILogger<PaymentProcessingSaga> _logger;
    private readonly SagaOptions _sagaOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State AwaitingAuthorization { get; private set; } = null!;
    public State AuthorizationCompleted { get; private set; } = null!;
    public State AuthorizationFailed { get; private set; } = null!;
    public State AwaitingCapture { get; private set; } = null!;
    public State PaymentCompleted { get; private set; } = null!;
    public State PaymentFailed { get; private set; } = null!;
    public State VoidInProgress { get; private set; } = null!;
    public State VoidCompleted { get; private set; } = null!;
    public State VoidFailed { get; private set; } = null!;
    public State RefundInProgress { get; private set; } = null!;
    public State RefundCompleted { get; private set; } = null!;
    public State RefundFailed { get; private set; } = null!;

    // Events
    public Event<PaymentInitiatedEvent> PaymentInitiated { get; private set; } = null!;
    public Event<PaymentAuthorizedEvent> PaymentAuthorized { get; private set; } = null!;
    public Event<PaymentAuthorizationFailedEvent> PaymentAuthorizationFailed { get; private set; } = null!;
    public Event<PaymentCapturedEvent> PaymentCaptured { get; private set; } = null!;
    public Event<PaymentCaptureFailedEvent> PaymentCaptureFailed { get; private set; } = null!;
    public Event<PaymentVoidedEvent> PaymentVoided { get; private set; } = null!;
    public Event<PaymentRefundCompletedEvent> RefundCompleted_ { get; private set; } = null!;
    public Event<RequestPaymentRefundCommand> RefundRequested { get; private set; } = null!;

    // Schedules
    public Schedule<PaymentSagaState, AuthorizationTimeoutExpired> AuthorizationTimeout { get; private set; } = null!;
    public Schedule<PaymentSagaState, CaptureTimeoutExpired> CaptureTimeout { get; private set; } = null!;
    public Schedule<PaymentSagaState, VoidTimeoutExpired> VoidTimeout { get; private set; } = null!;
    public Schedule<PaymentSagaState, RefundTimeoutExpired> RefundTimeout { get; private set; } = null!;

    public PaymentProcessingSaga(
        ILogger<PaymentProcessingSaga> logger,
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
        Event(() => PaymentInitiated, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new PaymentSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
        });

        Event(() => PaymentAuthorized, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => PaymentAuthorizationFailed, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => PaymentCaptured, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => PaymentCaptureFailed, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => PaymentVoided, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => RefundCompleted_, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => RefundRequested, e => e.CorrelateById(ctx => ctx.Message.CorrelationId));
    }

    private void ConfigureSchedules()
    {
        Schedule(() => AuthorizationTimeout, instance => instance.AuthorizationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentTimeouts.AuthorizationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => CaptureTimeout, instance => instance.CaptureTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentTimeouts.CaptureMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => VoidTimeout, instance => instance.VoidTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentTimeouts.VoidMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => RefundTimeout, instance => instance.RefundTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentTimeouts.RefundMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });
    }

    private void ConfigureStateMachine()
    {
        InstanceState(x => x.CurrentState);

        ConfigureInitialState();
        ConfigureAwaitingAuthorizationState();
        ConfigureAwaitingCaptureState();
        ConfigurePaymentCompletedState();
        ConfigureVoidInProgressState();
        ConfigureRefundInProgressState();

        SetCompletedWhenFinalized();
    }

    private void ConfigureInitialState()
    {
        Initially(
            When(PaymentInitiated)
                .Then(InitializeSagaState)
                .Activity(x => x.OfType<PaymentSagaStartedActivity>())
                .PublishAsync(ctx => ctx.Init<RequestPaymentAuthorizationCommand>(
                    new RequestPaymentAuthorizationCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentMethodId = ctx.Saga.PaymentMethodId,
                        Amount = ctx.Saga.Amount,
                        Currency = ctx.Saga.Currency,
                        IdempotencyKey = ctx.Saga.IdempotencyKey,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(AuthorizationTimeout,
                    ctx => new AuthorizationTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingAuthorization));
    }

    private void ConfigureAwaitingAuthorizationState()
    {
        During(AwaitingAuthorization,
            When(PaymentAuthorized)
                .Then(HandleAuthorizationCompleted)
                .Activity(x => x.OfType<AuthorizationCompletedActivity>())
                .Unschedule(AuthorizationTimeout)
                .PublishAsync(ctx => ctx.Init<RequestPaymentCaptureCommand>(
                    new RequestPaymentCaptureCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Amount = ctx.Saga.Amount,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(CaptureTimeout,
                    ctx => new CaptureTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingCapture),
            When(PaymentAuthorizationFailed)
                .Then(HandleAuthorizationFailed)
                .Activity(x => x.OfType<AuthorizationFailedActivity>())
                .Unschedule(AuthorizationTimeout)
                .IfElse(ctx => ctx.Message.IsRetryable && ctx.Saga.AuthorizationRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.AuthorizationRetryCount++)
                        .PublishAsync(ctx => ctx.Init<RequestPaymentAuthorizationCommand>(
                            new RequestPaymentAuthorizationCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentMethodId = ctx.Saga.PaymentMethodId,
                                Amount = ctx.Saga.Amount,
                                Currency = ctx.Saga.Currency,
                                IdempotencyKey = ctx.Saga.IdempotencyKey,
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
                        .Schedule(AuthorizationTimeout,
                            ctx => new AuthorizationTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            }),
                    noRetry => noRetry
                        .TransitionTo(AuthorizationFailed)
                        .Finalize()),
            When(AuthorizationTimeout.Received)
                .Then(HandleAuthorizationTimeout)
                .Activity(x => x.OfType<AuthorizationTimeoutActivity>())
                .TransitionTo(AuthorizationFailed)
                .Finalize());
    }

    private void ConfigureAwaitingCaptureState()
    {
        During(AwaitingCapture,
            When(PaymentCaptured)
                .Then(HandleCaptureCompleted)
                .Activity(x => x.OfType<CaptureCompletedActivity>())
                .Unschedule(CaptureTimeout)
                .PublishAsync(ctx => ctx.Init<Finance.Payments.PaymentCompletedEvent>(
                    new Finance.Payments.PaymentCompletedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Amount = new Avro.AvroDecimal(ctx.Saga.Amount),
                        Currency = ctx.Saga.Currency,
                        CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .TransitionTo(PaymentCompleted),
            When(PaymentCaptureFailed)
                .Then(HandleCaptureFailed)
                .Activity(x => x.OfType<CaptureFailedActivity>())
                .Unschedule(CaptureTimeout)
                .IfElse(ctx => ctx.Message.IsRetryable && ctx.Saga.CaptureRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.CaptureRetryCount++)
                        .PublishAsync(ctx => ctx.Init<RequestPaymentCaptureCommand>(
                            new RequestPaymentCaptureCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                AuthorizationId = ctx.Saga.AuthorizationId!,
                                Amount = ctx.Saga.Amount,
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
                        .Schedule(CaptureTimeout,
                            ctx => new CaptureTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            }),
                    noRetry => noRetry
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishAsync(ctx => ctx.Init<RequestPaymentVoidCommand>(
                            new RequestPaymentVoidCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                AuthorizationId = ctx.Saga.AuthorizationId!,
                                Reason = $"Capture failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
                        .PublishAsync(ctx => ctx.Init<Finance.Payments.PaymentFailedEvent>(
                            new Finance.Payments.PaymentFailedEvent
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                ErrorCode = ctx.Message.ErrorCode,
                                ErrorMessage = ctx.Message.ErrorMessage,
                                FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            }))
                        .Schedule(VoidTimeout,
                            ctx => new VoidTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            })
                        .TransitionTo(VoidInProgress)),
            When(CaptureTimeout.Received)
                .Then(HandleCaptureTimeout)
                .Activity(x => x.OfType<CaptureTimeoutActivity>())
                .Then(ctx => ctx.Saga.CompensationTriggered = true)
                .PublishAsync(ctx => ctx.Init<RequestPaymentVoidCommand>(
                    new RequestPaymentVoidCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Reason = "Capture timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .PublishAsync(ctx => ctx.Init<Finance.Payments.PaymentFailedEvent>(
                    new Finance.Payments.PaymentFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = "CAPTURE_TIMEOUT",
                        ErrorMessage = "Capture timeout expired",
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(VoidTimeout,
                    ctx => new VoidTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(VoidInProgress));
    }

    /// <summary>
    /// PaymentCompleted state: Payment is complete (captured). Saga remains alive to handle
    /// potential refund requests from business sagas if their downstream operations fail.
    /// </summary>
    private void ConfigurePaymentCompletedState()
    {
        During(PaymentCompleted,
            When(RefundRequested)
                .Then(HandleRefundRequested)
                .PublishAsync(ctx => ctx.Init<RequestPaymentRefundCommand>(
                    new RequestPaymentRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = ctx.Message.Reason,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    }))
                .Schedule(RefundTimeout,
                    ctx => new RefundTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(RefundInProgress));
    }

    private void ConfigureVoidInProgressState()
    {
        During(VoidInProgress,
            When(PaymentVoided)
                .Then(HandleVoidCompleted)
                .Activity(x => x.OfType<VoidCompletedActivity>())
                .Unschedule(VoidTimeout)
                .TransitionTo(VoidCompleted)
                .Finalize(),
            When(VoidTimeout.Received)
                .Then(HandleVoidTimeout)
                .Activity(x => x.OfType<VoidTimeoutActivity>())
                .TransitionTo(VoidFailed)
                .Finalize());
    }

    private void ConfigureRefundInProgressState()
    {
        During(RefundInProgress,
            When(RefundCompleted_)
                .Then(HandleRefundCompleted)
                .Activity(x => x.OfType<RefundCompletedActivity>())
                .Unschedule(RefundTimeout)
                .TransitionTo(RefundCompleted)
                .Finalize(),
            When(RefundTimeout.Received)
                .Then(HandleRefundTimeout)
                .Activity(x => x.OfType<RefundTimeoutActivity>())
                .TransitionTo(RefundFailed)
                .Finalize());
    }

    // Handler Methods

    private void InitializeSagaState(BehaviorContext<PaymentSagaState, PaymentInitiatedEvent> ctx)
    {
        var message = ctx.Message;
        ctx.Saga.UserId = message.UserId;
        ctx.Saga.PaymentMethodId = message.PaymentMethodId;
        ctx.Saga.Amount = message.Amount;
        ctx.Saga.Currency = message.Currency;
        ctx.Saga.IdempotencyKey = message.IdempotencyKey;
        ctx.Saga.InitiatedAtUtc = message.InitiatedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} initialized for user {UserId}, amount {Amount} {Currency}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Saga.Amount, ctx.Saga.Currency);
    }

    private void HandleAuthorizationCompleted(
        BehaviorContext<PaymentSagaState, PaymentAuthorizedEvent> ctx)
    {
        ctx.Saga.AuthorizationId = ctx.Message.AuthorizationId;
        ctx.Saga.AuthorizedAtUtc = ctx.Message.AuthorizedAtUtc;
        ctx.Saga.AuthorizationExpiresAtUtc = ctx.Message.ExpiresAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} authorization completed. AuthId: {AuthorizationId}",
            ctx.Saga.CorrelationId, ctx.Saga.AuthorizationId);
    }

    private void HandleAuthorizationFailed(
        BehaviorContext<PaymentSagaState, PaymentAuthorizationFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} authorization failed: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleAuthorizationTimeout(
        BehaviorContext<PaymentSagaState, AuthorizationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "AUTHORIZATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Authorization timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} authorization timed out for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleCaptureCompleted(
        BehaviorContext<PaymentSagaState, PaymentCapturedEvent> ctx)
    {
        ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
        ctx.Saga.CapturedAtUtc = ctx.Message.CapturedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} capture completed. TransactionId: {PaymentTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.PaymentTransactionId);
    }

    private void HandleCaptureFailed(
        BehaviorContext<PaymentSagaState, PaymentCaptureFailedEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} capture failed: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleCaptureTimeout(
        BehaviorContext<PaymentSagaState, CaptureTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "CAPTURE_TIMEOUT";
        ctx.Saga.ErrorMessage = "Capture timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} capture timed out for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleRefundRequested(
        BehaviorContext<PaymentSagaState, RequestPaymentRefundCommand> ctx)
    {
        ctx.Saga.CompensationTriggered = true;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} received refund request for user {UserId}. Reason: {Reason}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.Reason);
    }

    private void HandleVoidCompleted(
        BehaviorContext<PaymentSagaState, PaymentVoidedEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.VoidedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} void completed. AuthorizationId: {AuthorizationId}",
            ctx.Saga.CorrelationId, ctx.Message.AuthorizationId);
    }

    private void HandleVoidTimeout(
        BehaviorContext<PaymentSagaState, VoidTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "VOID_TIMEOUT";
        ctx.Saga.ErrorMessage = "Void timeout expired. Manual intervention required.";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Payment saga {CorrelationId} void timed out for user {UserId}. Manual intervention required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleRefundCompleted(
        BehaviorContext<PaymentSagaState, PaymentRefundCompletedEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.RefundedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} refund completed. RefundTransactionId: {RefundTransactionId}",
            ctx.Saga.CorrelationId, ctx.Message.RefundTransactionId);
    }

    private void HandleRefundTimeout(
        BehaviorContext<PaymentSagaState, RefundTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "REFUND_TIMEOUT";
        ctx.Saga.ErrorMessage = "Refund timeout expired. Manual intervention required.";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Payment saga {CorrelationId} refund timed out for user {UserId}. Manual intervention required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }
}

