using MassTransit;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Extensions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Observability.Activities;
using SagaOrchestrators.Payments.PaymentProcessingSaga.Schedules;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga;

/// <summary>
/// MassTransit state machine implementing the payment processing saga.
/// Orchestrates the complete payment lifecycle: initiation, authorization, capture,
/// activation, and compensation (void/refund).
/// </summary>
public sealed class PaymentProcessingSagaOrchestrator : MassTransitStateMachine<PaymentProcessingSagaState>
{
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State AwaitingAuthorization { get; private set; }
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

    public Schedule<PaymentProcessingSagaState, SuccessFinalizationTimeoutExpired> SuccessFinalizationTimeout
    {
        get;
        private set;
    }

    public PaymentProcessingSagaOrchestrator(
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
        InstanceState(sagaState => sagaState.CurrentState);

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
                .Then(ctx =>
                {
                    ctx.Saga.CorrelationId = ctx.Message.CorrelationId;
                    ctx.Saga.OrderId = ctx.Message.OrderId;
                    ctx.Saga.UserId = ctx.Message.UserId;
                    ctx.Saga.PaymentMethodId = ctx.Message.PaymentMethodId;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.Currency = ctx.Message.Currency;
                    ctx.Saga.IdempotencyKey = ctx.Message.IdempotencyKey;
                    ctx.Saga.InitiatedAtUtc = ctx.Message.InitiatedAtUtc;
                })
                .Activity(x => x.OfType<PaymentSagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AuthorizePaymentCommand
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        OrderId = ctx.Saga.OrderId,
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
                .Then(ctx =>
                {
                    ctx.Saga.AuthorizationId = ctx.Message.AuthorizationId;
                    ctx.Saga.AuthorizedAtUtc = ctx.Message.AuthorizedAtUtc;
                    ctx.Saga.AuthorizationExpiresAtUtc = ctx.Message.ExpiresAtUtc;
                })
                .Activity(x => x.OfType<AuthorizationCompletedActivity>())
                .Unschedule(AuthorizationTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
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
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
                    ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
                })
                .Activity(x => x.OfType<AuthorizationFailedActivity>())
                .Unschedule(AuthorizationTimeout)
                .IfElse(
                    ctx => ctx.Message.IsRetryable
                           && ctx.Saga.AuthorizationRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.AuthorizationRetryCount++)
                        .PublishToOutbox(
                            _topicsOptions.PaymentsPaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new AuthorizePaymentCommand
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                OrderId = ctx.Saga.OrderId,
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
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "AUTHORIZATION_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Authorization timeout expired";
                })
                .Activity(x => x.OfType<AuthorizationTimeoutActivity>())
                .TransitionTo(AuthorizationFailed)
                .Finalize());
    }

    private void ConfigureAwaitingCaptureState()
    {
        During(AwaitingCapture,
            When(PaymentCapturedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.PaymentTransactionId = ctx.Message.PaymentTransactionId;
                    ctx.Saga.CapturedAtUtc = ctx.Message.CapturedAtUtc;
                })
                .Activity(x => x.OfType<CaptureCompletedActivity>())
                .Unschedule(CaptureTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPayments,
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
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = ctx.Message.ErrorCode;
                    ctx.Saga.ErrorMessage = ctx.Message.ErrorMessage;
                })
                .Activity(x => x.OfType<CaptureFailedActivity>())
                .Unschedule(CaptureTimeout)
                .IfElse(ctx => ctx.Message.IsRetryable && ctx.Saga.CaptureRetryCount < _sagaOptions.MaxRetryAttempts,
                    retry => retry
                        .Then(ctx => ctx.Saga.CaptureRetryCount++)
                        .PublishToOutbox(
                            _topicsOptions.PaymentsPaymentCommands,
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
                            _topicsOptions.PaymentsPaymentCommands,
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
                            _topicsOptions.PaymentsPayments,
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
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "CAPTURE_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Capture timeout expired";
                })
                .Activity(x => x.OfType<CaptureTimeoutActivity>())
                .Then(ctx => ctx.Saga.CompensationTriggered = true)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
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
                    _topicsOptions.PaymentsPayments,
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
                .Then(ctx =>
                {
                    ctx.Saga.CompensationTriggered = true;
                })
                .Activity(x => x.OfType<RefundRequestedActivity>())
                .Unschedule(SuccessFinalizationTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
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
                .Activity(x => x.OfType<SuccessFinalizationActivity>())
                .Finalize());
    }

    private void ConfigureVoidInProgressState()
    {
        During(VoidInProgress,
            When(PaymentVoidedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CompensationCompletedAtUtc = ctx.Message.VoidedAtUtc;
                })
                .Activity(x => x.OfType<VoidCompletedActivity>())
                .Unschedule(VoidTimeout)
                .TransitionTo(VoidCompleted)
                .Finalize(),
            When(VoidTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "VOID_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Void timeout expired. Manual intervention required.";
                })
                .Activity(x => x.OfType<VoidTimeoutActivity>())
                .TransitionTo(VoidFailed)
                .Finalize());
    }

    private void ConfigureRefundInProgressState()
    {
        During(RefundInProgress,
            When(PaymentRefundCompletedEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CompensationCompletedAtUtc = ctx.Message.RefundedAtUtc;
                })
                .Activity(x => x.OfType<RefundCompletedActivity>())
                .Unschedule(RefundTimeout)
                .TransitionTo(RefundCompleted)
                .Finalize(),
            When(RefundTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = "REFUND_TIMEOUT";
                    ctx.Saga.ErrorMessage = "Refund timeout expired. Manual intervention required.";
                })
                .Activity(x => x.OfType<RefundTimeoutActivity>())
                .TransitionTo(RefundFailed)
                .Finalize());
    }

    private void ConfigureEvents()
    {
        Event(() => PaymentInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

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

        Event(() => PaymentVoidedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            // Compensation events can arrive after saga finalized
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
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.AuthorizationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => CaptureTimeout, instance => instance.CaptureTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.CaptureMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => VoidTimeout, instance => instance.VoidTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.VoidMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => RefundTimeout, instance => instance.RefundTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.RefundMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Schedule(() => SuccessFinalizationTimeout, instance => instance.SuccessFinalizationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.SuccessFinalizationMinutes);
            s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });
    }
}
