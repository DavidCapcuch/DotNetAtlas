using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Ordering acknowledges the cancel-order command during
/// compensation. One of the gating events for the transition <c>CompensatingStockReservations
/// -&gt; Compensated</c> per § 4 row 14.
/// </summary>
public sealed class OrderCancelledActivity : IStateMachineActivity<CheckoutSagaState, OrderCancelledSagaEvent>
{
    private readonly ILogger<OrderCancelledActivity> _logger;

    public OrderCancelledActivity(ILogger<OrderCancelledActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-cancelled-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderCancelledSagaEvent> context,
        IBehavior<CheckoutSagaState, OrderCancelledSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderCancelledActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, message.OrderId.ToString());
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} order cancelled during compensation. OrderId: {OrderId}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.OrderId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderCancelledSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, OrderCancelledSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
