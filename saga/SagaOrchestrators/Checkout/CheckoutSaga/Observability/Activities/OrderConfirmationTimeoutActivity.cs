using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when <c>OrderConfirmationTimeout</c> expires (transition
/// <c>AwaitingConfirmation -&gt; CompensatingPayment</c>) per
/// docs/bc-design/checkout-saga.md § 3. Increments
/// <c>saga.checkout.confirmation_timeout</c> counter.
/// </summary>
public sealed class OrderConfirmationTimeoutActivity
    : IStateMachineActivity<CheckoutSagaState, OrderConfirmationTimeoutExpired>
{
    private readonly ILogger<OrderConfirmationTimeoutActivity> _logger;

    public OrderConfirmationTimeoutActivity(ILogger<OrderConfirmationTimeoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-confirmation-timeout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderConfirmationTimeoutExpired> context,
        IBehavior<CheckoutSagaState, OrderConfirmationTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderConfirmationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, CheckoutSagaErrorCodes.ConfirmationTimeout);
            if (saga.OrderId is { } orderId)
            {
                activity.SetTag(CheckoutSagaActivityTags.OrderId, orderId.ToString());
            }
        }

        CheckoutSagaMetrics.RecordConfirmationTimeout();

        _logger.LogWarning(
            "{SagaType} {CorrelationId} order confirmation timeout fired - OrderConfirmedEvent never arrived for order {OrderId}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, saga.OrderId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderConfirmationTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, OrderConfirmationTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
