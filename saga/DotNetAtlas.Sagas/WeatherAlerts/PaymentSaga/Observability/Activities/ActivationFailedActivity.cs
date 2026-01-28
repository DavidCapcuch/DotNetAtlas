using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation fails.
/// </summary>
public sealed class ActivationFailedActivity : IStateMachineActivity<PaymentSagaState, PaymentActivationFailedEvent>
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
        BehaviorContext<PaymentSagaState, PaymentActivationFailedEvent> context,
        IBehavior<PaymentSagaState, PaymentActivationFailedEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(ActivationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.should_compensate", message.ShouldCompensate);
        }

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentActivationFailedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentActivationFailedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

