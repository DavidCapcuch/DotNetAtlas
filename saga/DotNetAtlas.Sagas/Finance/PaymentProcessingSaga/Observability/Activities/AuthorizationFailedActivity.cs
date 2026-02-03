using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization fails.
/// </summary>
public sealed class
    AuthorizationFailedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("authorization-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(PaymentSagaActivityTags.IsRetryable, message.IsRetryable);
        }

        PaymentSagaInstrumentation.RecordAuthorizationFailed(message.ErrorCode);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentAuthorizationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
