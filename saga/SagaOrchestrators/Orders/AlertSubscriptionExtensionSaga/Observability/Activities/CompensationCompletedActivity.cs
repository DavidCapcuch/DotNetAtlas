using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) completes successfully
/// for the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>.
/// </summary>
public sealed class CompensationCompletedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionCompensationCompletedSagaEvent>
{
    private readonly ILogger<CompensationCompletedActivity> _logger;

    public CompensationCompletedActivity(ILogger<CompensationCompletedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("compensation-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionCompensationCompletedSagaEvent>
            context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionCompensationCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(CompensationCompletedActivity), saga.CorrelationId,
            AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.RefundTransactionId, message.RefundTransactionId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordCompensationCompleted(
            duration, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId, message.RefundTransactionId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState,
                AlertSubscriptionExtensionCompensationCompletedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionCompensationCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
