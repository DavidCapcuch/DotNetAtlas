using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when activation times out.
/// </summary>
public sealed class ActivationTimeoutActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, ActivationTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, ActivationTimeoutExpired> context,
        IBehavior<SubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaTimeout(
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, ActivationTimeoutExpired, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
