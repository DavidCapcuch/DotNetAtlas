using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization completes.
/// </summary>
public sealed class
    AuthorizationCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent>
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
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(PaymentSagaActivityTags.AuthorizationId, context.Message.AuthorizationId);
        }

        PaymentSagaInstrumentation.RecordAuthorizationCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
