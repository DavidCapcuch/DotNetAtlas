using DotNetAtlas.Sagas.Common.Observability.Metrics;
using DotNetAtlas.Sagas.Common.Observability.Tracing;
using DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Schedules;
using MassTransit;
using static DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.AlertSubscriptionPurchaseSagaActivityTags;

namespace DotNetAtlas.Sagas.Orders.AlertSubscriptionPurchaseSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment times out
/// for the <see cref="AlertSubscriptionPurchaseSagaOrchestrator"/>.
/// </summary>
public sealed class
    PaymentTimeoutActivity : IStateMachineActivity<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired>
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PaymentTimeoutActivity> _logger;

    public PaymentTimeoutActivity(TimeProvider timeProvider, ILogger<PaymentTimeoutActivity> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("payment-timeout-activity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(
        BehaviorContext<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;
        var duration = _timeProvider.GetUtcNow() - saga.CreatedUtc;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(PaymentTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypePurchase);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SubscriptionTier, saga.SubscriptionTier.ToString());
            activity.SetTag(SagaActivityTags.DurationMs, duration.TotalMilliseconds);
        }

        AlertSubscriptionSagaMetrics.RecordPaymentTimeout(AlertSubscriptionSagaMetrics.SagaTypePurchase);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} timed out waiting for payment response for user {UserId}",
            nameof(AlertSubscriptionPurchaseSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionPurchaseSagaState, PaymentTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
