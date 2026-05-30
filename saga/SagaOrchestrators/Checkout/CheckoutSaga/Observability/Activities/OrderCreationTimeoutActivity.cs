using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when <c>OrderCreationTimeout</c> expires (transition
/// <c>AwaitingOrderCreation -&gt; Failed</c>) per docs/bc-design/checkout-saga.md § 3.
/// Increments <c>saga.checkout.order_creation_timeout</c> counter.
/// </summary>
public sealed class OrderCreationTimeoutActivity
    : IStateMachineActivity<CheckoutSagaState, OrderCreationTimeoutExpired>
{
    private readonly ILogger<OrderCreationTimeoutActivity> _logger;

    public OrderCreationTimeoutActivity(ILogger<OrderCreationTimeoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-creation-timeout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderCreationTimeoutExpired> context,
        IBehavior<CheckoutSagaState, OrderCreationTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderCreationTimeoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, CheckoutSagaErrorCodes.OrderCreationTimeout);
        }

        CheckoutSagaMetrics.RecordOrderCreationTimeout();

        _logger.LogWarning(
            "{SagaType} {CorrelationId} order creation timeout fired - OrderCreatedEvent never arrived within budget",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderCreationTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, OrderCreationTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
