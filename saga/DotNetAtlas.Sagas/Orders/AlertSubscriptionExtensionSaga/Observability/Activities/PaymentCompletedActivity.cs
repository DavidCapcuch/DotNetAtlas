using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when payment completes successfully.
/// </summary>
public sealed class PaymentCompletedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent>
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
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentCompletedActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, context.Message.PaymentTransactionId.ToString());
            activity.SetTag(SagaActivityTags.PaymentDurationMs, duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordPaymentCompleted(
            duration, SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent,
            TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
