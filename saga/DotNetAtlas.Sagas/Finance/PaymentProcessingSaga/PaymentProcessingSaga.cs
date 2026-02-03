using DotNetAtlas.Sagas.Common.Config;
using DotNetAtlas.Sagas.Common.Extensions;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using Finance.Payments;
using MassTransit;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga;

/// <summary>
/// MassTransit state machine implementing the payment processing saga.
/// Orchestrates the complete payment lifecycle: initiation, authorization, capture,
/// activation, and compensation (void/refund).
/// </summary>
public sealed class PaymentProcessingSaga : MassTransitStateMachine<PaymentProcessingSagaState>
{
    private readonly ILogger<PaymentProcessingSaga> _logger;
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State AwaitingAuthorization { get; private set; }
    public State AuthorizationCompleted { get; private set; }
    public State AuthorizationFailed { get; private set; }
    public State AwaitingCapture { get; private set; }
    public State PaymentCompleted { get; private set; }
    public State PaymentFailed { get; private set; }
    public State VoidInProgress { get; private set; }
    public State VoidCompleted { get; private set; }
    public State VoidFailed { get; private set; }
    public State RefundInProgress { get; private set; }
    public State RefundCompleted { get; private set; }
    public State RefundFailed { get; private set; }

    // Events
    public Event<PaymentInitiatedSagaEvent> PaymentInitiatedEvent { get; private set; }
    public Event<PaymentAuthorizedSagaEvent> PaymentAuthorizedEvent { get; private set; }
    public Event<PaymentAuthorizationFailedSagaEvent> PaymentAuthorizationFailedEvent { get; private set; }
    public Event<PaymentCapturedSagaEvent> PaymentCapturedEvent { get; private set; }
    public Event<PaymentCaptureFailedSagaEvent> PaymentCaptureFailedEvent { get; private set; }
    public Event<PaymentVoidedSagaEvent> PaymentVoidedEvent { get; private set; }
    public Event<PaymentRefundCompletedSagaEvent> PaymentRefundCompletedEvent { get; private set; }
    public Event<PaymentRefundRequestedSagaEvent> PaymentRefundRequestedEvent { get; private set; }

    // Schedules
    public Schedule<PaymentProcessingSagaState, AuthorizationTimeoutExpired> AuthorizationTimeout { get; private set; }
    public Schedule<PaymentProcessingSagaState, CaptureTimeoutExpired> CaptureTimeout { get; private set; }
    public Schedule<PaymentProcessingSagaState, VoidTimeoutExpired> VoidTimeout { get; private set; }
    public Schedule<PaymentProcessingSagaState, RefundTimeoutExpired> RefundTimeout { get; private set; }
    public Schedule<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired> SuccessFinalizationTimeout { get; private set; }

    public PaymentProcessingSaga(
        ILogger<PaymentProcessingSaga> logger,
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
        Event(() => PaymentInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.InsertOnInitial = true;
            e.SetSagaFactory(ctx => new PaymentProcessingSagaState
            {
                CorrelationId = ctx.Message.CorrelationId,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
        });

        // Intermediate events - missing saga indicates a bug (event arrived for non-existent saga)
        Event(() => PaymentAuthorizedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentAuthorizationFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentCapturedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentCaptureFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Fault());
        });

        // Compensation events - can legitimately arrive after saga finalized
        Event(() => PaymentVoidedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentRefundCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        // Refund request - can arrive after saga finalized, discard silently
        Event(() => PaymentRefundRequestedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });
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

        Schedule(() => SuccessFinalizationTimeout, instance => instance.SuccessFinalizationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentTimeouts.SuccessFinalizationMinutes);
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
            When(PaymentInitiatedEvent)
                .Then(InitializeSagaState)
                .Activity(x => x.OfType<PaymentSagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AuthorizePaymentCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentMethodId = ctx.Saga.PaymentMethodId,
                        Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                        Currency = ctx.Saga.Currency,
                        IdempotencyKey = ctx.Saga.IdempotencyKey,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
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
            When(PaymentAuthorizedEvent)
                .Then(HandleAuthorizationCompleted)
                .Activity(x => x.OfType<AuthorizationCompletedActivity>())
                .Unschedule(AuthorizationTimeout)
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new CapturePaymentCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(CaptureTimeout,
                    ctx => new CaptureTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingCapture),
            When(PaymentAuthorizationFailedEvent)
                .Then(HandleAuthorizationFailed)
                .Activity(x => x.OfType<AuthorizationFailedActivity>())
                .Unschedule(AuthorizationTimeout)
                .IfElse(
                    ctx => ctx.Message.IsRetryable && ctx.Saga.AuthorizationRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.AuthorizationRetryCount++)
                        .PublishToOutbox(
                            _topicsOptions.FinancePaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new AuthorizePaymentCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                PaymentMethodId = ctx.Saga.PaymentMethodId,
                                Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                                Currency = ctx.Saga.Currency,
                                IdempotencyKey = ctx.Saga.IdempotencyKey,
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
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
            When(PaymentCapturedEvent)
                .Then(HandleCaptureCompleted)
                .Activity(x => x.OfType<CaptureCompletedActivity>())
                .Unschedule(CaptureTimeout)
                .PublishToOutbox(
                    _topicsOptions.FinancePayments,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new PaymentCompletedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                        Currency = ctx.Saga.Currency,
                        CompletedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(SuccessFinalizationTimeout,
                    ctx => new SuccessFinalizationTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(PaymentCompleted),
            When(PaymentCaptureFailedEvent)
                .Then(HandleCaptureFailed)
                .Activity(x => x.OfType<CaptureFailedActivity>())
                .Unschedule(CaptureTimeout)
                .IfElse(ctx => ctx.Message.IsRetryable && ctx.Saga.CaptureRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.CaptureRetryCount++)
                        .PublishToOutbox(
                            _topicsOptions.FinancePaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new CapturePaymentCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                AuthorizationId = ctx.Saga.AuthorizationId!,
                                Amount = ctx.Saga.Amount.ToAvroDecimal(4),
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
                        .Schedule(CaptureTimeout,
                            ctx => new CaptureTimeoutExpired
                            {
                                CorrelationId = ctx.Saga.CorrelationId
                            }),
                    noRetry => noRetry
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishToOutbox(
                            _topicsOptions.FinancePaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new VoidPaymentCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                AuthorizationId = ctx.Saga.AuthorizationId!,
                                Reason = $"Capture failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
                        .PublishToOutbox(
                            _topicsOptions.FinancePayments,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new PaymentFailedEvent
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                ErrorCode = ctx.Message.ErrorCode,
                                ErrorMessage = ctx.Message.ErrorMessage,
                                FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                            })
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
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new VoidPaymentCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Reason = "Capture timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .PublishToOutbox(
                    _topicsOptions.FinancePayments,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new PaymentFailedEvent
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        ErrorCode = "CAPTURE_TIMEOUT",
                        ErrorMessage = "Capture timeout expired",
                        FailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(VoidTimeout,
                    ctx => new VoidTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(VoidInProgress));
    }

    /// <summary>
    /// PaymentCompleted state: Payment is complete (captured). Saga waits for potential refund
    /// requests from business sagas if their downstream operations fail. After the success
    /// finalization timeout, the saga finalizes and late refunds must go through a separate service.
    /// </summary>
    private void ConfigurePaymentCompletedState()
    {
        During(PaymentCompleted,
            When(PaymentRefundRequestedEvent)
                .Then(HandleRefundRequested)
                .Unschedule(SuccessFinalizationTimeout)
                .PublishToOutbox(
                    _topicsOptions.FinancePaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new RequestRefundCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        Reason = ctx.Message.Reason,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(RefundTimeout,
                    ctx => new RefundTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(RefundInProgress),
            When(SuccessFinalizationTimeout.Received)
                .Then(ctx => _logger.LogInformation(
                    "Payment saga {CorrelationId} finalizing after success timeout - no refund requested",
                    ctx.Saga.CorrelationId))
                .Finalize());
    }

    private void ConfigureVoidInProgressState()
    {
        During(VoidInProgress,
            When(PaymentVoidedEvent)
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
            When(PaymentRefundCompletedEvent)
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

    private void InitializeSagaState(BehaviorContext<PaymentProcessingSagaState, PaymentInitiatedSagaEvent> ctx)
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
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> ctx)
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
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} authorization failed: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleAuthorizationTimeout(
        BehaviorContext<PaymentProcessingSagaState, AuthorizationTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "AUTHORIZATION_TIMEOUT";
        ctx.Saga.ErrorMessage = "Authorization timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} authorization timed out for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleCaptureCompleted(
        BehaviorContext<PaymentProcessingSagaState, PaymentCapturedSagaEvent> ctx)
    {
        ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
        ctx.Saga.CapturedAtUtc = ctx.Message.CapturedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} capture completed. TransactionId: {PaymentTransactionId}",
            ctx.Saga.CorrelationId, ctx.Saga.PaymentTransactionId);
    }

    private void HandleCaptureFailed(
        BehaviorContext<PaymentProcessingSagaState, PaymentCaptureFailedSagaEvent> ctx)
    {
        ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
        ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} capture failed: {ErrorCode} - {ErrorMessage}",
            ctx.Saga.CorrelationId, ctx.Message.ErrorCode, ctx.Message.ErrorMessage);
    }

    private void HandleCaptureTimeout(
        BehaviorContext<PaymentProcessingSagaState, CaptureTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "CAPTURE_TIMEOUT";
        ctx.Saga.ErrorMessage = "Capture timeout expired";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogWarning(
            "Payment saga {CorrelationId} capture timed out for user {UserId}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleRefundRequested(
        BehaviorContext<PaymentProcessingSagaState, PaymentRefundRequestedSagaEvent> ctx)
    {
        ctx.Saga.CompensationTriggered = true;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} received refund request for user {UserId}. Reason: {Reason}",
            ctx.Saga.CorrelationId, ctx.Saga.UserId, ctx.Message.Reason);
    }

    private void HandleVoidCompleted(
        BehaviorContext<PaymentProcessingSagaState, PaymentVoidedSagaEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.VoidedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} void completed. AuthorizationId: {AuthorizationId}",
            ctx.Saga.CorrelationId, ctx.Message.AuthorizationId);
    }

    private void HandleVoidTimeout(
        BehaviorContext<PaymentProcessingSagaState, VoidTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "VOID_TIMEOUT";
        ctx.Saga.ErrorMessage = "Void timeout expired. Manual intervention required.";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Payment saga {CorrelationId} void timed out for user {UserId}. Manual intervention required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }

    private void HandleRefundCompleted(
        BehaviorContext<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> ctx)
    {
        ctx.Saga.CompensationCompletedAtUtc = ctx.Message.RefundedAtUtc;
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogInformation(
            "Payment saga {CorrelationId} refund completed. RefundTransactionId: {RefundTransactionId}",
            ctx.Saga.CorrelationId, ctx.Message.RefundTransactionId);
    }

    private void HandleRefundTimeout(
        BehaviorContext<PaymentProcessingSagaState, RefundTimeoutExpired> ctx)
    {
        ctx.Saga.ErrorCode = "REFUND_TIMEOUT";
        ctx.Saga.ErrorMessage = "Refund timeout expired. Manual intervention required.";
        ctx.Saga.LastUpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        _logger.LogError(
            "Payment saga {CorrelationId} refund timed out for user {UserId}. Manual intervention required",
            ctx.Saga.CorrelationId, ctx.Saga.UserId);
    }
}
