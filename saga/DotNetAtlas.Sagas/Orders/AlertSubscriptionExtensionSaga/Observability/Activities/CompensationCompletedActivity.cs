using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) completes successfully
/// for the <see cref="AlertSubscriptionExtensionSaga"/>.
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
        var duration = DateTime.UtcNow - saga.CreatedAtUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationCompletedActivity), saga.CorrelationId,
            AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.RefundTransactionId, message.RefundTransactionId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordCompensationCompleted(
            duration, AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            nameof(AlertSubscriptionExtensionSaga), saga.CorrelationId, saga.UserId, message.RefundTransactionId);

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
