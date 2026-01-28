using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when a saga starts.
/// </summary>
public sealed class SagaStartedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(SagaStartedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.duration_days", saga.DurationDays);
        }

        SubscriptionSagaInstrumentation.RecordSagaStarted(
            saga.SubscriptionTier.ToString(),
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchaseInitiatedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
