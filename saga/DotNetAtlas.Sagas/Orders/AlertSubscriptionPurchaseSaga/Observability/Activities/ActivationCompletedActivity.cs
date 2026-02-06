using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when subscription activation completes successfully
/// for the <see cref="AlertSubscriptionPurchaseSaga"/>.
/// </summary>
public sealed class
    ActivationCompletedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionActivatedSagaEvent>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActivationCompletedActivity> _logger;

    public ActivationCompletedActivity(TimeProvider timeProvider, ILogger<ActivationCompletedActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-completed-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivatedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivatedSagaEvent> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationCompletedActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier,
                saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordSagaCompleted(
            duration, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} completed successfully for user {UserId}",
            nameof(AlertSubscriptionPurchaseSaga), context.Saga.CorrelationId, context.Saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivatedSagaEvent, TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionActivatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
