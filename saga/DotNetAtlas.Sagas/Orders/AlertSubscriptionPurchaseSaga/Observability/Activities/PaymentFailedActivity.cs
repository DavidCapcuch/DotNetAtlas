using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment fails
/// for the <see cref="AlertSubscriptionPurchaseSaga"/>.
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
        var duration = _timeProvider.GetUtcNow() - saga.CreatedAtUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentFailedActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ErrorMessage, message.ErrorMessage);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordPaymentFailed(
            message.ErrorCode, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            nameof(AlertSubscriptionPurchaseSaga), saga.CorrelationId, saga.UserId, message.ErrorCode, message.ErrorMessage);

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
