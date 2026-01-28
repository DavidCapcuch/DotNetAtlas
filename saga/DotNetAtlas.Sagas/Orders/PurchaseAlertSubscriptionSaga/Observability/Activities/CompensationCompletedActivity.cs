using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.PurchaseAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when compensation completes successfully.
/// </summary>
public sealed class CompensationCompletedActivity : IStateMachineActivity<SubscriptionPurchaseSagaState, SubscriptionPurchaseCompensationCompletedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("compensation-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionPurchaseSagaState, SubscriptionPurchaseCompensationCompletedEvent> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchaseCompensationCompletedEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.refund_transaction_id", message.RefundTransactionId.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordCompensationCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionPurchaseSagaState, SubscriptionPurchaseCompensationCompletedEvent, TException> context,
        IBehavior<SubscriptionPurchaseSagaState, SubscriptionPurchaseCompensationCompletedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
