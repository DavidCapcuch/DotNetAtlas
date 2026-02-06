using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using MassTransit;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when compensation (refund) times out
/// for the <see cref="AlertSubscriptionPurchaseSaga"/>. This indicates a critical failure
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

        using var activity = AlertSubscriptionSagaInstrumentation.StartActivity(
            nameof(CompensationTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaInstrumentation.RecordCompensationTimeout(
            duration, AlertSubscriptionSagaInstrumentation.SagaTypePurchase);

        _logger.LogError(
            "{SagaType} {CorrelationId} compensation timed out for user {UserId}. Manual intervention may be required",
            nameof(AlertSubscriptionPurchaseSaga), saga.CorrelationId, saga.UserId);

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
