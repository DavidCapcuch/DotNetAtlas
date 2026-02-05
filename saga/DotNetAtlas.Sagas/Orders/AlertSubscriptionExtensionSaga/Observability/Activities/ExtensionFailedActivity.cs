using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when subscription extension fails
/// for the <see cref="AlertSubscriptionExtensionSaga"/>.
/// </summary>
public sealed class ExtensionFailedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent>
{
    private readonly ILogger<ExtensionFailedActivity> _logger;

    public ExtensionFailedActivity(ILogger<ExtensionFailedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(ExtensionFailedActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ShouldCompensate, message.ShouldCompensate);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordSagaFailed(
            message.ErrorCode, duration, AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} extension failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            nameof(AlertSubscriptionExtensionSaga), saga.CorrelationId, saga.UserId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
