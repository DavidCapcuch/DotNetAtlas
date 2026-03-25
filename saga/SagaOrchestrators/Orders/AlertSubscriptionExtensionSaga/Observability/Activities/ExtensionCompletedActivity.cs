using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using static SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability.
    AlertSubscriptionExtensionSagaActivityTags;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when subscription extension completes successfully
/// for the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>.
/// </summary>
public sealed class ExtensionCompletedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent>
{
    private readonly ILogger<ExtensionCompletedActivity> _logger;

    public ExtensionCompletedActivity(ILogger<ExtensionCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("extension-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(ExtensionCompletedActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(DurationDays, saga.DurationDays);
            activity.SetTag(NewExpiresAt, context.Message.NewExpiresAtUtc.ToString("O"));
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordSagaCompleted(
            duration, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} completed successfully for user {UserId}. New expiry: {NewExpiresAtUtc}",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.NewExpiresAtUtc);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtendedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
