using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when activation completes successfully.
/// </summary>
public sealed class
    ActivationCompletedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent>
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
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionActivatedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
