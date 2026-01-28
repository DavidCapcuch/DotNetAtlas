using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment void completes.
/// </summary>
public sealed class VoidCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentVoidedEvent>
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
        BehaviorContext<PaymentSagaState, PaymentVoidedEvent> context,
        IBehavior<PaymentSagaState, PaymentVoidedEvent> next)
    {
        var saga = context.Saga;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(VoidCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.authorization_id", context.Message.AuthorizationId);
        }

        PaymentSagaInstrumentation.RecordVoidCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentVoidedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentVoidedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

