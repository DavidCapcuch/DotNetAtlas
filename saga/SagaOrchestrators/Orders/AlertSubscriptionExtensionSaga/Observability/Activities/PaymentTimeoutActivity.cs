using MassTransit;
using SagaOrchestrators.Common.Observability.Metrics;
using SagaOrchestrators.Common.Observability.Tracing;
using SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Schedules;

namespace SagaOrchestrators.Orders.AlertSubscriptionExtensionSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when payment times out
/// for the <see cref="AlertSubscriptionExtensionSagaOrchestrator"/>.
/// </summary>
public sealed class PaymentTimeoutActivity
    : IStateMachineActivity<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired>
{
    private readonly ILogger<PaymentTimeoutActivity> _logger;

    public PaymentTimeoutActivity(ILogger<PaymentTimeoutActivity> logger)
    {
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
        BehaviorContext<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> context,
        IBehavior<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity = AlertSubscriptionSagaMetrics.StartActivity(
            nameof(PaymentTimeoutActivity), saga.CorrelationId, AlertSubscriptionSagaMetrics.SagaTypeExtension);

        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
        }

        AlertSubscriptionSagaMetrics.RecordPaymentTimeout(AlertSubscriptionSagaMetrics.SagaTypeExtension);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} timed out waiting for payment response for user {UserId}",
            nameof(AlertSubscriptionExtensionSagaOrchestrator), saga.CorrelationId, saga.UserId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<AlertSubscriptionExtensionSagaState, PaymentTimeoutExpired> next)
        where TException : Exception
    {
        return next.Faulted(context);
    }
}
