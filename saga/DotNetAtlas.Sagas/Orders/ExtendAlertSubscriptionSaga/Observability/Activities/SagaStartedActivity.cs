using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when an extension saga starts.
/// </summary>
public sealed class SagaStartedActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedSagaEvent>
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedSagaEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(SagaStartedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.duration_days", saga.DurationDays);
        }

        SubscriptionSagaInstrumentation.RecordSagaStarted(
            SubscriptionSagaInstrumentation.SagaTypeExtension,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedSagaEvent, TException>
            context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
