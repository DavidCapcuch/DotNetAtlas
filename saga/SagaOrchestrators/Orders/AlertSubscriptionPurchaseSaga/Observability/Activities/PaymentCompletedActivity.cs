using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment completes successfully
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// </summary>
public sealed class
    PaymentCompletedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchasePaymentCompletedSagaEvent>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentCompletedActivity> _logger;

    public PaymentCompletedActivity(TimeProvider timeProvider, ILogger<PaymentCompletedActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(PaymentCompletedActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.PaymentTransactionId, context.Message.PaymentTransactionId.ToString());
            activity.SetTag(SagaActivityTags.PaymentDurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordPaymentCompleted(
            duration, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} payment completed for user {UserId}. TransactionId: {PaymentTransactionId}",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.PaymentTransactionId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent,
            TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchasePaymentCompletedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
