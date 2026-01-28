using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PaymentSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when a payment saga starts.
/// This is a "dumb" payment saga activity - it only records payment-specific telemetry.
/// </summary>
public sealed class PaymentSagaStartedActivity : IStateMachineActivity<PaymentSagaState, PaymentInitiatedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<PaymentSagaState, PaymentInitiatedEvent> context,
        IBehavior<PaymentSagaState, PaymentInitiatedEvent> next)
    {
        var saga = context.Saga;

        using var activity = PaymentSagaInstrumentation.StartActivity(nameof(PaymentSagaStartedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.amount", saga.Amount);
            activity.SetTag("saga.currency", saga.Currency);
        }

        PaymentSagaInstrumentation.RecordSagaStarted(saga.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<PaymentSagaState, PaymentInitiatedEvent, TException> context,
        IBehavior<PaymentSagaState, PaymentInitiatedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

