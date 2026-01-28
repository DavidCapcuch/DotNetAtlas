using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.WeatherAlerts.ExtendAlertSubscriptionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when extension times out.
/// </summary>
public sealed class ExtensionTimeoutActivity : IStateMachineActivity<SubscriptionExtensionSagaState, ExtensionTimeoutExpired>
{
    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<SubscriptionExtensionSagaState, ExtensionTimeoutExpired> context,
        IBehavior<SubscriptionExtensionSagaState, ExtensionTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ExtensionTimeoutActivity),
            saga.CorrelationId,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag("saga.user_id", saga.UserId.ToString());
            activity.SetTag("saga.duration_ms", duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaTimeout(
            duration,
            SubscriptionSagaInstrumentation.SagaTypeExtension);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<SubscriptionExtensionSagaState, ExtensionTimeoutExpired, TException> context,
        IBehavior<SubscriptionExtensionSagaState, ExtensionTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}

