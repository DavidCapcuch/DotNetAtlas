using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/> starts
/// processing a new subscription extension request.
/// </summary>
public sealed class SagaStartedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionInitiatedSagaEvent>
{
    private readonly ILogger<SagaStartedActivity> _logger;

    public SagaStartedActivity(ILogger<SagaStartedActivity> logger)
    {
        _logger = logger;
    }

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

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(SagaStartedActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionExtensionSagaActivityTags.DurationDays, saga.DurationDays);
        }

        AlertSubscriptionSagaMetrics.RecordSagaStarted(
            AlertSubscriptionSagaMetrics.SagaTypeExtension, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} initialized for user {UserId}, duration {DurationDays} days",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.DurationDays);

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
