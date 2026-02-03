using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics and traces when activation completes successfully.
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
        _logger.LogInformation(
            "Saga {CorrelationId} completed successfully for user {UserId}",
            context.Saga.CorrelationId, context.Saga.UserId);

        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedAtUtc;

        using var activity = SubscriptionSagaInstrumentation.StartActivity(
            nameof(ActivationCompletedActivity), saga.CorrelationId, SubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier,
                saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        SubscriptionSagaInstrumentation.RecordSagaCompleted(
            duration, SubscriptionSagaInstrumentation.SagaTypePurchase);

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
