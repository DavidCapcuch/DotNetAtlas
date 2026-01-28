using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when an extension fails.
/// </summary>
public sealed class ExtensionFailedActivity
    : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtensionFailedSagaEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionFailedSagaEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ExtensionFailedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.error_code", message.ErrorCode);
            activity.SetTag("saga.should_compensate", message.ShouldCompensate);
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaFailed(
            message.ErrorCode,
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtensionFailedSagaEvent, TException>
            context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
