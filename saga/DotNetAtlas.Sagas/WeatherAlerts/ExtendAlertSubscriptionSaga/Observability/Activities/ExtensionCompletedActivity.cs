using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Events;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when extension completes successfully.
/// </summary>
public sealed class ExtensionCompletedActivity : IStateMachineActivity<SubscriptionExtensionSagaState, SubscriptionExtendedEvent>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, SubscriptionExtendedEvent> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtendedEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ExtensionCompletedActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.duration_days", saga.DurationDays);
            activity.SetTag("saga.new_expires_at", context.Message.NewExpiresAtUtc.ToString("O"));
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaCompleted(
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, SubscriptionExtendedEvent, TException> context,
        IBehavior<SubscriptionExtensionSagaState, SubscriptionExtendedEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

