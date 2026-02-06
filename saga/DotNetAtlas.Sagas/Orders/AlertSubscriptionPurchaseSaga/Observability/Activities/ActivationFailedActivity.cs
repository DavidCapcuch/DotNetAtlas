using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when activation fails
/// for the <see cref="AlertSubscriptionPurchaseSaga"/>.
/// </summary>
public sealed class ActivationFailedActivity
    : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivationFailedSagaEvent>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActivationFailedActivity> _logger;

    public ActivationFailedActivity(TimeProvider timeProvider, ILogger<ActivationFailedActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-failed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivationFailedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivationFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationFailedActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
            activity.SetTag(SagaActivityTags.ShouldCompensate, message.ShouldCompensate);
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordSagaFailed(
            message.ErrorCode, duration, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} activation failed for user {UserId}: {ErrorCode} - {ErrorMessage}",
            nameof(AlertSubscriptionPurchaseSaga), saga.CorrelationId, saga.UserId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivationFailedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivationFailedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
