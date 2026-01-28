using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment authorization fails.
/// </summary>
public sealed class
    AuthorizationFailedActivity : IStateMachineActivity<PaymentSagaState, PaymentAuthorizationFailedSagaEvent>
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
        BehaviorContext<PaymentSagaState, PaymentAuthorizationFailedSagaEvent> context,
        IBehavior<PaymentSagaState, PaymentAuthorizationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(AuthorizationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.is_retryable", message.IsRetryable);
        }

        PaymentSagaInstrumentation.RecordAuthorizationFailed(message.ErrorCode);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentAuthorizationFailedSagaEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentAuthorizationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
