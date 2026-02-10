using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when activation times out
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// </summary>
public sealed class
    ActivationTimeoutActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActivationTimeoutActivity> _logger;

    public ActivationTimeoutActivity(TimeProvider timeProvider, ILogger<ActivationTimeoutActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("activation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(ActivationTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(AlertSubscriptionPurchaseSagaActivityTags.SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordSagaTimeout(
            duration, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} timed out waiting for activation response for user {UserId}",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, ActivationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
