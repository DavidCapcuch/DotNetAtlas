using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment void times out.
/// </summary>
public sealed class VoidTimeoutActivity : IStateMachineActivity<PaymentProcessingSagaState, VoidTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("void-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, VoidTimeoutExpired> context,
        IBehavior<PaymentProcessingSagaState, VoidTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(VoidTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.TimeoutStage, "void");
        }

        PaymentSagaInstrumentation.RecordSagaTimeout("void", duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, VoidTimeoutExpired, TException> context,
        IBehavior<PaymentProcessingSagaState, VoidTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
