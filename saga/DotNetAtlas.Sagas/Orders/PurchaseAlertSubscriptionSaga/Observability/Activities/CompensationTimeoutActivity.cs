using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when compensation times out.
/// </summary>
public sealed class CompensationTimeoutActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, CompensationTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("compensation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, CompensationTimeoutExpired> context,
        IBehavior<SubscriptionPurchaseSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordCompensationTimeout(
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, CompensationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
