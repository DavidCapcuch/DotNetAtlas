using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires on entry to the abnormal-terminal <c>CompensationStuck</c> state.
/// Emits <c>saga.checkout.stuck</c> AND <c>saga.checkout.compensation_timeout</c> counters
/// + a high-severity log line carrying the runbook fields per
/// docs/bc-design/saga-stuck-runbook.md § 3 (order_id, last_state, stuck_since_utc,
/// failure_reason).
/// </summary>
public sealed class CheckoutStuckActivity
    : IStateMachineActivity<CheckoutSagaState, CompensationTimeoutExpired>
{
    private readonly ILogger<CheckoutStuckActivity> _logger;

    public CheckoutStuckActivity(ILogger<CheckoutStuckActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("checkout-stuck-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, CompensationTimeoutExpired> context,
        IBehavior<CheckoutSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var lastState = saga.CurrentState;
        var errorCode = saga.ErrorCode ?? CheckoutSagaErrorCodes.CompensationTimeout;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(CheckoutStuckActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, errorCode);
            activity.SetTag(CheckoutSagaActivityTags.LastState, lastState);
        }

        // Note: saga.checkout.compensation_timeout is incremented by the preceding
        // CompensationTimeoutActivity in the action chain - we only emit the stuck-specific
        // counter here to avoid double-counting the timeout fire.
        CheckoutSagaMetrics.RecordStuck(lastState, errorCode);

        _logger.LogError(
            "{SagaType} {CorrelationId} STUCK in {LastState} - errorCode={ErrorCode} stuckSinceUtc={StuckSinceUtc} reason={Reason}",
            nameof(CheckoutSagaOrchestrator),
            saga.CorrelationId,
            lastState,
            errorCode,
            saga.CompensationStartedAtUtc,
            saga.ErrorMessage ?? "(none)");

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, CompensationTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
