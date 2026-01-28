using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment fails.
/// </summary>
public sealed class PaymentFailedActivity : IStateMachineActivity<SubscriptionExtensionSagaState, PaymentFailedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, PaymentFailedEvent> context,
        IBehavior<SubscriptionExtensionSagaState, PaymentFailedEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentFailedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.error_message", message.ErrorMessage);
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentFailed(
            message.ErrorCode,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, PaymentFailedEvent, TException> context,
        IBehavior<SubscriptionExtensionSagaState, PaymentFailedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

