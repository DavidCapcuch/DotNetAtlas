using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation fails.
/// </summary>
public sealed class ActivationFailedActivity : IStateMachineActivity<PaymentSagaState, PaymentActivationFailedSagaEvent>
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
        BehaviorContext<PaymentSagaState, PaymentActivationFailedSagaEvent> context,
        IBehavior<PaymentSagaState, PaymentActivationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(ActivationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.should_compensate", message.ShouldCompensate);
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentActivationFailedSagaEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentActivationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
