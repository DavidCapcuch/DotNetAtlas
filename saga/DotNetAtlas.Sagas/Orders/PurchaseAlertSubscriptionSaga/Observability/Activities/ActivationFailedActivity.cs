using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when activation fails.
/// </summary>
public sealed class ActivationFailedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationFailedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.should_compensate", message.ShouldCompensate);
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaFailed(
            message.ErrorCode,
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionActivationFailedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
