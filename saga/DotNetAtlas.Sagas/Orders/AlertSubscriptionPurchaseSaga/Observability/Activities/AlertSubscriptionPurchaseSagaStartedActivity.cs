using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when a saga starts.
/// </summary>
public sealed class
    AlertSubscriptionPurchaseSagaStartedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchaseInitiatedSagaEvent>
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
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(AlertSubscriptionPurchaseSagaStartedActivity), saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier,
                saga.SubscriptionTier.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.DurationDays, saga.DurationDays);
        }

        SubscriptionSagaInstrumentation.RecordSagaStarted(
            saga.SubscriptionTier.ToString(), SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent,
                TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
