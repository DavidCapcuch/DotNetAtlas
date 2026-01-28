using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization completes.
/// </summary>
public sealed class AuthorizationCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentAuthorizedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, PaymentAuthorizedEvent> context,
        IBehavior<PaymentSagaState, PaymentAuthorizedEvent> next)
    {
        var saga = context.Saga;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.authorization_id", context.Message.AuthorizationId);
        }

        PaymentSagaInstrumentation.RecordAuthorizationCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentAuthorizedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentAuthorizedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

