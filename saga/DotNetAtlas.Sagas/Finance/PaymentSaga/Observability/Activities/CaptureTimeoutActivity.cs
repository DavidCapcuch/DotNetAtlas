using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment capture times out.
/// </summary>
public sealed class CaptureTimeoutActivity : IStateMachineActivity<PaymentSagaState, CaptureTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, CaptureTimeoutExpired> context,
        IBehavior<PaymentSagaState, CaptureTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(CaptureTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.timeout_stage", "capture");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("capture", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, CaptureTimeoutExpired, TException> context,
        IBehavior<PaymentSagaState, CaptureTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
