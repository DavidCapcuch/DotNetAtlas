using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment capture times out.
/// </summary>
public sealed class CaptureTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, CaptureTimeoutExpired>
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
        BehaviorContext<PaymentProcessingSagaState, CaptureTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, CaptureTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(CaptureTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "capture");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("capture", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, CaptureTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, CaptureTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
