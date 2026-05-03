using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when <c>CompensationTimeout</c> expires inside one of the
/// compensating states. Increments <c>saga.checkout.compensation_timeout</c> counter
/// tagged with the last state per docs/bc-design/checkout-saga.md § 11.2.
/// </summary>
/// <remarks>
/// Distinct from <see cref="CheckoutStuckActivity"/>: this one runs as the
/// <em>activity</em> on the timeout transition (counts the fire); the stuck activity
/// covers the <c>Stuck</c> terminal entry counter (<c>saga.checkout.stuck</c>). The two
/// are used together when the saga lands in <c>CompensationStuck</c>.
/// </remarks>
public sealed class CompensationTimeoutActivity
    : IStateMachineActivity<CheckoutSagaState, CompensationTimeoutExpired>
{
    private readonly ILogger<CompensationTimeoutActivity> _logger;

    public CompensationTimeoutActivity(ILogger<CompensationTimeoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("compensation-timeout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, CompensationTimeoutExpired> context,
        IBehavior<CheckoutSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var lastState = saga.CurrentState;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(CompensationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, "COMPENSATION_TIMEOUT");
            activity.SetTag(CheckoutSagaActivityTags.LastState, lastState);
        }

        CheckoutSagaMetrics.RecordCompensationTimeout(lastState);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} compensation timeout fired in state {LastState}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, lastState);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, CompensationTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
