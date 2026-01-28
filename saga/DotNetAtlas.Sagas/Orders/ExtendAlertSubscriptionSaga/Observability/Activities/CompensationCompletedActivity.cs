using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when compensation completes successfully.
/// </summary>
public sealed class CompensationCompletedActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, ExtensionCompensationCompletedSagaEvent>
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
        BehaviorContext<SubscriptionExtensionSagaState, ExtensionCompensationCompletedSagaEvent> context,
        IBehavior<SubscriptionExtensionSagaState, ExtensionCompensationCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.refund_transaction_id", message.RefundTransactionId.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordCompensationCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, ExtensionCompensationCompletedSagaEvent, TException>
            context,
        IBehavior<SubscriptionExtensionSagaState, ExtensionCompensationCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
