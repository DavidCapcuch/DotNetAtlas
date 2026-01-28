using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when subscription activation completes.
/// </summary>
public sealed class
    ActivationCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentActivationCompletedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, PaymentActivationCompletedSagaEvent> context,
        IBehavior<PaymentSagaState, PaymentActivationCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.InitiatedAtUtc;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(ActivationCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.payment_transaction_id", saga.PaymentTransactionId?.ToString());
        }

        PaymentSagaInstrumentation.RecordSagaCompleted(duration);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentActivationCompletedSagaEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentActivationCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
