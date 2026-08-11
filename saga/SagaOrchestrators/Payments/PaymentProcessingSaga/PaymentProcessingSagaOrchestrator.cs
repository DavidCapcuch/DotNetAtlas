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
/// MassTransit state machine implementing the payment processing saga (ADR-0026 capture pivot).
/// Orchestrates the payment lifecycle: authorize → await the Checkout saga's capture approval →
/// capture → complete, with a pre-capture void on the compensation path. The sub-saga issues
/// commands and reacts to events only — per ADR-0026 the Payments service owns and publishes all
/// payment-state integration events (including the terminal <c>PaymentCompletedEvent</c> /
/// <c>PaymentFailedEvent</c>); the sub-saga publishes none of them.
/// </summary>
public sealed class PaymentProcessingSagaOrchestrator : MassTransitStateMachine<PaymentProcessingSagaState>
{
    private readonly SagaOptions _sagaOptions;
    private readonly SagaTopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;

    // States
    public State AwaitingAuthorization { get; private set; }
    public State AuthorizationFailed { get; private set; }
    public State AwaitingCaptureApproval { get; private set; }
    public State AwaitingCapture { get; private set; }
    public State PaymentCompleted { get; private set; }
    public State VoidInProgress { get; private set; }
    public State VoidCompleted { get; private set; }
    public State VoidFailed { get; private set; }

    // Events
    public Event<PaymentInitiatedSagaEvent> PaymentInitiatedEvent { get; private set; }
    public Event<PaymentAuthorizedSagaEvent> PaymentAuthorizedEvent { get; private set; }
    public Event<PaymentAuthorizationFailedSagaEvent> PaymentAuthorizationFailedEvent { get; private set; }
    public Event<ApproveCaptureSagaEvent> ApproveCaptureEvent { get; private set; }
    public Event<AbortCaptureSagaEvent> AbortCaptureEvent { get; private set; }
    public Event<PaymentCapturedSagaEvent> PaymentCapturedEvent { get; private set; }
    public Event<PaymentCaptureFailedSagaEvent> PaymentCaptureFailedEvent { get; private set; }
    public Event<PaymentVoidedSagaEvent> PaymentVoidedEvent { get; private set; }

    // Schedules
    public Schedule<PaymentProcessingSagaState, AuthorizationTimeoutExpired> AuthorizationTimeout { get; private set; }

    public Schedule<PaymentProcessingSagaState, CaptureApprovalTimeoutExpired> CaptureApprovalTimeout
    {
        get;
        private set;
    }

    public Schedule<PaymentProcessingSagaState, CaptureTimeoutExpired> CaptureTimeout { get; private set; }
    public Schedule<PaymentProcessingSagaState, VoidTimeoutExpired> VoidTimeout { get; private set; }

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
        ConfigureAwaitingCaptureApprovalState();
        ConfigureAwaitingCaptureState();
        ConfigureVoidInProgressState();

        SetCompletedWhenFinalized();
    }

    private void ConfigureInitialState()
    {
        Initially(
            When(PaymentInitiatedEvent)
                .Then(ctx =>
                {
                    // ADR-0029: the saga is keyed on the pre-assigned OrderId via
                    // CorrelateById(m => m.OrderId), so CorrelationId == OrderId from birth.
                    ctx.Saga.UserId = ctx.Message.UserId;
                    ctx.Saga.PaymentMethodId = ctx.Message.PaymentMethodId;
                    ctx.Saga.Amount = ctx.Message.Amount;
                    ctx.Saga.Currency = ctx.Message.Currency;
                    ctx.Saga.IdempotencyKey = ctx.Message.IdempotencyKey;
                    ctx.Saga.InitiatedAtUtc = ctx.Message.InitiatedAtUtc;
                    // Cross-cutting #255: mint the Payments aggregate's PK up front
                    // so the AuthorizePaymentCommand wire contract carries it, retries reuse it, and
                    // the v7 PK guarantee on PaymentTransaction.Id is genuine. PaymentTransactionId
                    // stays distinct from the saga key (OrderId) — one-payment-per-order is enforced
                    // by the unique index on payment_transactions.order_id (ADR-0029).
                    ctx.Saga.PaymentTransactionId = Guid.CreateVersion7();
                })
                .Activity(x => x.OfType<PaymentSagaStartedActivity>())
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new AuthorizePaymentCommand
                    {
                        PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                        OrderId = ctx.Saga.CorrelationId,
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

    /// <summary>
    /// AwaitingAuthorization: on success the sub-saga parks in <see cref="AwaitingCaptureApproval"/>
    /// and waits for the Checkout saga's capture-approval / abort signal (ADR-0026). Capture is
    /// deferred to the pivot — it is NOT triggered here.
    /// </summary>
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
                .Schedule(CaptureApprovalTimeout,
                    ctx => new CaptureApprovalTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(AwaitingCaptureApproval),
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
                                // Reuse the PaymentTransactionId minted at initial state — the
                                // Payments aggregate identifies the same row across retries
                                // (one-payment-per-saga; idempotent re-authorize).
                                PaymentTransactionId = ctx.Saga.PaymentTransactionId!.Value,
                                OrderId = ctx.Saga.CorrelationId,
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
                        // ADR-0026: Payments already published the terminal PaymentFailedEvent on
                        // the decline (it owns the terminal). The sub-saga just finalizes.
                        .TransitionTo(AuthorizationFailed)
                        .Finalize()),
            When(AuthorizationTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = PaymentProcessingSagaErrorCodes.AuthorizationTimeout;
                    ctx.Saga.ErrorMessage = "Authorization timeout expired";
                })
                .Activity(x => x.OfType<AuthorizationTimeoutActivity>())
                .TransitionTo(AuthorizationFailed)
                .Finalize());
    }

    /// <summary>
    /// AwaitingCaptureApproval (ADR-0026 capture-pivot wait-state): the authorization is held while
    /// the Checkout saga confirms stock + order. A capture-approval signal drives capture; an abort
    /// signal or the wait-state timeout drives the (free, pre-capture) void path.
    /// </summary>
    private void ConfigureAwaitingCaptureApprovalState()
    {
        During(AwaitingCaptureApproval,
            When(ApproveCaptureEvent)
                .Activity(x => x.OfType<CaptureApprovedActivity>())
                .Unschedule(CaptureApprovalTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new CapturePaymentCommand
                    {
                        OrderId = ctx.Saga.CorrelationId,
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
            When(AbortCaptureEvent)
                .Then(ctx =>
                {
                    ctx.Saga.CompensationTriggered = true;
                    ctx.Saga.ErrorMessage = ctx.Message.Reason;
                })
                .Activity(x => x.OfType<CaptureAbortedActivity>())
                .Unschedule(CaptureApprovalTimeout)
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new VoidPaymentCommand
                    {
                        OrderId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Reason = ctx.Message.Reason,
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(VoidTimeout,
                    ctx => new VoidTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(VoidInProgress),
            When(CaptureApprovalTimeout.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ErrorCode = PaymentProcessingSagaErrorCodes.CaptureApprovalTimeout;
                    ctx.Saga.ErrorMessage = "Capture approval timeout expired";
                    ctx.Saga.CompensationTriggered = true;
                })
                .Activity(x => x.OfType<CaptureApprovalTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new VoidPaymentCommand
                    {
                        OrderId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Reason = "Capture approval timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(VoidTimeout,
                    ctx => new VoidTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(VoidInProgress));
    }

    /// <summary>
    /// AwaitingCapture: after capture approval the sub-saga issued <c>CapturePaymentCommand</c> and
    /// waits for the outcome. On capture success the saga reaches its successful terminal and
    /// finalizes — it does NOT publish <c>PaymentCompletedEvent</c> (ADR-0026: Payments owns the
    /// terminal). On capture failure / timeout it drives the void path.
    /// </summary>
    private void ConfigureAwaitingCaptureState()
    {
        During(AwaitingCapture,
            When(PaymentCapturedEvent)
                .Then(ctx =>
                {
                    // #255: PaymentTransactionId was minted in the Initial state and
                    // travelled out on AuthorizePaymentCommand. Payments echoes the same id back on
                    // PaymentCapturedEvent. They MUST be equal — any divergence is a Payments-side bug
                    // or a wire-shape skew. Fail loud rather than overwrite.
                    if (ctx.Saga.PaymentTransactionId != ctx.Message.PaymentTransactionId)
                    {
                        throw new InvalidOperationException(
                            $"PaymentTransactionId mismatch for OrderId {ctx.Saga.CorrelationId}: "
                            + $"saga minted {ctx.Saga.PaymentTransactionId}, Payments returned {ctx.Message.PaymentTransactionId}. "
                            + "Refusing to silently overwrite saga state — this indicates a wire-shape or Payments-side bug.");
                    }

                    ctx.Saga.CapturedAtUtc = ctx.Message.CapturedAtUtc;
                })
                .Activity(x => x.OfType<CaptureCompletedActivity>())
                .Unschedule(CaptureTimeout)
                // ADR-0026: the Payments service publishes the terminal PaymentCompletedEvent via
                // its own outbox. The sub-saga simply reaches its successful terminal and finalizes
                // — refund is a deferred customer/admin flow, so there is no post-completion wait.
                .TransitionTo(PaymentCompleted)
                .Finalize(),
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
                                OrderId = ctx.Saga.CorrelationId,
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
                        // ADR-0026: Payments published the terminal PaymentFailedEvent on the
                        // capture decline; the sub-saga just voids the (uncaptured) authorization.
                        .Then(ctx => ctx.Saga.CompensationTriggered = true)
                        .PublishToOutbox(
                            _topicsOptions.PaymentsPaymentCommands,
                            ctx => ctx.Saga.CorrelationId.ToString(),
                            ctx => new VoidPaymentCommand
                            {
                                OrderId = ctx.Saga.CorrelationId,
                                UserId = ctx.Saga.UserId,
                                AuthorizationId = ctx.Saga.AuthorizationId!,
                                Reason = $"Capture failed: {ctx.Message.ErrorMessage}",
                                RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
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
                    ctx.Saga.ErrorCode = PaymentProcessingSagaErrorCodes.CaptureTimeout;
                    ctx.Saga.ErrorMessage = "Capture timeout expired";
                    ctx.Saga.CompensationTriggered = true;
                })
                .Activity(x => x.OfType<CaptureTimeoutActivity>())
                .PublishToOutbox(
                    _topicsOptions.PaymentsPaymentCommands,
                    ctx => ctx.Saga.CorrelationId.ToString(),
                    ctx => new VoidPaymentCommand
                    {
                        OrderId = ctx.Saga.CorrelationId,
                        UserId = ctx.Saga.UserId,
                        AuthorizationId = ctx.Saga.AuthorizationId!,
                        Reason = "Capture timeout expired",
                        RequestedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                    })
                .Schedule(VoidTimeout,
                    ctx => new VoidTimeoutExpired
                    {
                        CorrelationId = ctx.Saga.CorrelationId
                    })
                .TransitionTo(VoidInProgress));
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
                    ctx.Saga.ErrorCode = PaymentProcessingSagaErrorCodes.VoidTimeout;
                    ctx.Saga.ErrorMessage = "Void timeout expired. Manual intervention required.";
                })
                .Activity(x => x.OfType<VoidTimeoutActivity>())
                .TransitionTo(VoidFailed)
                .Finalize());
    }

    private void ConfigureEvents()
    {
        Event(() => PaymentInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
        });

        Event(() => PaymentAuthorizedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentAuthorizationFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });

        // Capture-approval / abort signals from the Checkout saga (ADR-0026). They can arrive for
        // an already-finalized saga (e.g. the wait-state timed out and voided first), so discard
        // silently rather than fault on a missing instance.
        Event(() => ApproveCaptureEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => AbortCaptureEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentCapturedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentCaptureFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });

        Event(() => PaymentVoidedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.OrderId);
            // Compensation events can arrive after saga finalized
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

        Schedule(() => CaptureApprovalTimeout, instance => instance.CaptureApprovalTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(_sagaOptions.PaymentProcessingTimeouts.CaptureApprovalMinutes);
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
    }
}
