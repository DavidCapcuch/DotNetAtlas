using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentProcessingSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment refund completes.
/// </summary>
public sealed class
    RefundCompletedActivity : IStateMachineActivity<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("refund-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            PaymentSagaInstrumentation.StartActivity(nameof(RefundCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.RefundTransactionId, context.Message.RefundTransactionId.ToString());
        }

        PaymentSagaInstrumentation.RecordRefundCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent, TException> context,
        IBehavior<PaymentProcessingSagaState, PaymentRefundCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
