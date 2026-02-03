using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when an extension saga starts.
/// </summary>
public sealed class SagaStartedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(SagaStartedActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionExtensionSagaActivityTags.DurationDays, saga.DurationDays);
        }

        SubscriptionSagaInstrumentation.RecordSagaStarted(
            SubscriptionSagaInstrumentation.SagaTypeExtension, SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent,
                TException>
            context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
