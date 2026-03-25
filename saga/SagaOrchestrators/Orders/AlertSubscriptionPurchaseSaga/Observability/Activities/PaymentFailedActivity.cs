using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment fails
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// </summary>
public sealed class
    PaymentFailedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchasePaymentFailedSagaEvent>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentFailedActivity> _logger;

    public PaymentFailedActivity(TimeProvider timeProvider, ILogger<PaymentFailedActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(PaymentFailedActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ErrorMessage, message.ErrorMessage);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordPaymentFailed(
            message.ErrorCode, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
