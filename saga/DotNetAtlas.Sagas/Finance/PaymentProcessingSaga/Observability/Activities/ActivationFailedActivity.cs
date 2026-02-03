using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation fails.
/// </summary>
public sealed class
    ActivationFailedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentActivationFailedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentActivationFailedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(ActivationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ShouldCompensate, message.ShouldCompensate);
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentActivationFailedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentActivationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
