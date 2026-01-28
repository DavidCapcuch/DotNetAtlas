using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment times out.
/// </summary>
public sealed class
    PaymentTimeoutActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, PaymentTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, PaymentTimeoutExpired> context,
        IBehavior<SubscriptionPurchaseSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentTimeout(SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, PaymentTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

