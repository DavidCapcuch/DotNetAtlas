using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment refund completes.
/// </summary>
public sealed class RefundCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentRefundCompletedEvent>
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
        BehaviorContext<PaymentSagaState, PaymentRefundCompletedEvent> context,
        IBehavior<PaymentSagaState, PaymentRefundCompletedEvent> next)
    {
        var saga = context.Saga;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(RefundCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.refund_transaction_id", context.Message.RefundTransactionId.ToString());
        }

        PaymentSagaInstrumentation.RecordRefundCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentRefundCompletedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentRefundCompletedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

