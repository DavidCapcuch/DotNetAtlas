using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when extension fails.
/// </summary>
public sealed class ExtensionFailedActivity : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent>
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
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent> next)
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
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent, TException> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtensionFailedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

