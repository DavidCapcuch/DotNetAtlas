using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment completes successfully.
/// </summary>
public sealed class
    PaymentCompletedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, PaymentCompletedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, PaymentCompletedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, PaymentCompletedEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.payment_transaction_id", context.Message.PaymentTransactionId.ToString());
            activity.SetTag("saga.payment_duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, PaymentCompletedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, PaymentCompletedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

