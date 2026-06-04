using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Ordering reports the order created. Records the order-creation
/// duration histogram per § 11.2.
/// </summary>
public sealed class OrderCreatedActivity : IStateMachineActivity<CheckoutSagaState, OrderCreatedSagaEvent>
{
    private readonly ILogger<OrderCreatedActivity> _logger;

    public OrderCreatedActivity(ILogger<OrderCreatedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-created-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderCreatedSagaEvent> context,
        IBehavior<CheckoutSagaState, OrderCreatedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderCreatedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
        }

        CheckoutSagaMetrics.RecordOrderCreationDuration(message.OrderCreatedAtUtc - saga.InitiatedAtUtc);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} order created",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderCreatedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, OrderCreatedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
