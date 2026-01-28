using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment refund times out.
/// </summary>
public sealed class RefundTimeoutActivity : IStateMachineActivity<PaymentSagaState, RefundTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("refund-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, RefundTimeoutExpired> context,
        IBehavior<PaymentSagaState, RefundTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(RefundTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.timeout_stage", "refund");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("refund", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, RefundTimeoutExpired, TException> context,
        IBehavior<PaymentSagaState, RefundTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
