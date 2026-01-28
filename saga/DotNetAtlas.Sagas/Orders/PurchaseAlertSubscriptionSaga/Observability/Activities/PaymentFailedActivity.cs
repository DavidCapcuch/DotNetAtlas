using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment fails.
/// </summary>
public sealed class
    PaymentFailedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, SubscriptionPurchasePaymentFailedEvent>
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
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionPurchasePaymentFailedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchasePaymentFailedEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentFailedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.subscription_tier", saga.SubscriptionTier.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.error_message", message.ErrorMessage);
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentFailed(
            message.ErrorCode,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, SubscriptionPurchasePaymentFailedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchasePaymentFailedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
