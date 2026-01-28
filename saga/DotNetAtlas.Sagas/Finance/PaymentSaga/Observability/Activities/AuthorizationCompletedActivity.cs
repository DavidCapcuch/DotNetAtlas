using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization completes.
/// </summary>
public sealed class AuthorizationCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentAuthorizedSagaEvent>
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
        BehaviorContext<PaymentSagaState, PaymentAuthorizedSagaEvent> context,
        IBehavior<PaymentSagaState, PaymentAuthorizedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.authorization_id", context.Message.AuthorizationId);
        }

        PaymentSagaInstrumentation.RecordAuthorizationCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentAuthorizedSagaEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentAuthorizedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
