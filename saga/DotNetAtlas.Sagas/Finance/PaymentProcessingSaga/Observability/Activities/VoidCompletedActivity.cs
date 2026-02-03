using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment void completes.
/// </summary>
public sealed class VoidCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentVoidedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("void-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentVoidedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentVoidedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(VoidCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, context.Message.AuthorizationId);
        }

        PaymentSagaInstrumentation.RecordVoidCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentVoidedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentVoidedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
