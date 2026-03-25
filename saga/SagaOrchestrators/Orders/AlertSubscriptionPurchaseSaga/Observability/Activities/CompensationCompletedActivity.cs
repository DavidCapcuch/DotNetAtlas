using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) completes successfully
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// </summary>
public sealed class CompensationCompletedActivity
    : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseCompensationCompletedSagaEvent>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompensationCompletedActivity> _logger;

    public CompensationCompletedActivity(TimeProvider timeProvider, ILogger<CompensationCompletedActivity> logger)
    {
        _timeProvider = timeProvider;
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
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseCompensationCompletedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseCompensationCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(CompensationCompletedActivity), saga.CorrelationId,
            AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.RefundTransactionId, message.RefundTransactionId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordCompensationCompleted(
            duration, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} compensation completed for user {UserId}, refund transaction {RefundTransactionId}",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId, message.RefundTransactionId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseCompensationCompletedSagaEvent,
            TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseCompensationCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
