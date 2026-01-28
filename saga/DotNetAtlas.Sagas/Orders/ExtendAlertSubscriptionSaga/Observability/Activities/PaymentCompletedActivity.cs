using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment completes successfully.
/// </summary>
public sealed class PaymentCompletedActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentCompletedSagaEvent>
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentCompletedSagaEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.payment_transaction_id", context.Message.PaymentTransactionId.ToString());
            activity.SetTag("saga.payment_duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentCompletedSagaEvent,
            TException> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionPaymentCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
