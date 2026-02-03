using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment fails.
/// </summary>
public sealed class
    PaymentFailedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchasePaymentFailedSagaEvent>
{
    private readonly TimeProvider _timeProvider;

    public PaymentFailedActivity(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentFailedActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ErrorMessage, message.ErrorMessage);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentFailed(
            message.ErrorCode, SubscriptionSagaInstrumentation.SagaTypePurchase);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
