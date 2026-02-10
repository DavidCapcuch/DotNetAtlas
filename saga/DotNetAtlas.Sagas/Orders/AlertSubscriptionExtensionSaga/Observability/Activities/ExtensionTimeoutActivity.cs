using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when subscription extension times out
/// for the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>.
/// </summary>
public sealed class ExtensionTimeoutActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired>
{
    private readonly ILogger<ExtensionTimeoutActivity> _logger;

    public ExtensionTimeoutActivity(ILogger<ExtensionTimeoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> context,
        IBehavior<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(ExtensionTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordSagaTimeout(
            duration, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} timed out waiting for extension response for user {UserId}",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, ExtensionTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
