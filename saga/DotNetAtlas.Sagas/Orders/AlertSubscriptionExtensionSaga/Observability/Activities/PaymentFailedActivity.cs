using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment fails
/// for the <see cref="AlertSubscriptionExtensionSaga"/>.
/// </summary>
public sealed class PaymentFailedActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent>
{
    private readonly ILogger<PaymentFailedActivity> _logger;

    public PaymentFailedActivity(ILogger<PaymentFailedActivity> logger)
    {
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
        BehaviorContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = DateTime.UtcNow - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(PaymentFailedActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ErrorMessage, message.ErrorMessage);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordPaymentFailed(
            message.ErrorCode, AlertSubscriptionSagaInstrumentation.SagaTypeExtension);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} payment failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            nameof(AlertSubscriptionExtensionSaga), saga.CorrelationId, saga.UserId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent,
            TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, AlertSubscriptionExtensionPaymentFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
