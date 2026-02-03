using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment completes successfully.
/// </summary>
public sealed class
    PaymentCompletedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchasePaymentCompletedSagaEvent>
{
    private readonly TimeProvider _timeProvider;

    public PaymentCompletedActivity(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentCompletedActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, context.Message.PaymentTransactionId.ToString());
            activity.SetTag(SagaActivityTags.PaymentDurationMs, duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentCompleted(
            duration, SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent,
            TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
