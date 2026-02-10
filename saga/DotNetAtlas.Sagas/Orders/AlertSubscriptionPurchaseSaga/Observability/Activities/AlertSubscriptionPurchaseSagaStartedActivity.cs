using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.InternalSagaEvents;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/> starts
/// processing a new subscription purchase request.
/// </summary>
public sealed class
    AlertSubscriptionPurchaseSagaStartedActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState,
    AlertSubscriptionPurchaseInitiatedSagaEvent>
{
    private readonly ILogger<AlertSubscriptionPurchaseSagaStartedActivity> _logger;

    public AlertSubscriptionPurchaseSagaStartedActivity(ILogger<AlertSubscriptionPurchaseSagaStartedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("saga-started-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(AlertSubscriptionPurchaseSagaStartedActivity), saga.CorrelationId,
            AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier,
                saga.SubscriptionTier.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.DurationDays, saga.DurationDays);
        }

        AlertSubscriptionSagaMetrics.RecordSagaStarted(
            saga.SubscriptionTier.ToString(), AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} initialized for user {UserId}, tier {Tier}, duration {DurationDays} days",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.SubscriptionTier, saga.DurationDays);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent,
                TException>
            context,
        IBehavior<AlertSubscriptionPurchaseSagaState, AlertSubscriptionPurchaseInitiatedSagaEvent> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
