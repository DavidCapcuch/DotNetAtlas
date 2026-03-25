using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Schedules;

namespace SagaOrchestrators.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) times out
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>. This indicates a critical failure
/// that may require manual intervention.
/// </summary>
public sealed class
    CompensationTimeoutActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompensationTimeoutActivity> _logger;

    public CompensationTimeoutActivity(TimeProvider timeProvider, ILogger<CompensationTimeoutActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("compensation-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(CompensationTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordCompensationTimeout(
            duration, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogError(
            "{SagaType} {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, CompensationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
