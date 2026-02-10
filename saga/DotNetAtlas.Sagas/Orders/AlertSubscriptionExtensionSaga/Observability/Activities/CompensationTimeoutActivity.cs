using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) times out
/// for the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>. This indicates a critical failure
/// that may require manual intervention.
/// </summary>
public sealed class CompensationTimeoutActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired>
{
    private readonly ILogger<CompensationTimeoutActivity> _logger;

    public CompensationTimeoutActivity(ILogger<CompensationTimeoutActivity> logger)
    {
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
        BehaviorContext<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> context,
        IBehavior<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = DateTime.UtcNow - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(CompensationTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordCompensationTimeout(
            duration, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogError(
            "{SagaType} {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, CompensationTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
