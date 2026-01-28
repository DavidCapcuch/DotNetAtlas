using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment fails.
/// </summary>
public sealed class PaymentFailedActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentFailedSagaEvent>
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentFailedSagaEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentFailedSagaEvent> next)
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
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentFailedSagaEvent,
            TException> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
