using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment capture completes.
/// </summary>
public sealed class CaptureCompletedActivity : IStateMachineActivity<PaymentSagaState, PaymentCapturedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("capture-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, PaymentCapturedEvent> context,
        IBehavior<PaymentSagaState, PaymentCapturedEvent> next)
    {
        var saga = context.Saga;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(CaptureCompletedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.payment_transaction_id", context.Message.PaymentTransactionId.ToString());
        }

        PaymentSagaInstrumentation.RecordCaptureCompleted();

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentCapturedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentCapturedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

