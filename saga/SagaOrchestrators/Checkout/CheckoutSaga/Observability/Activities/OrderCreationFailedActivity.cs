using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Ordering rejects the create-order command (transition
/// <c>AwaitingOrderCreation -&gt; Failed</c>) per § 4 transition table row 3.
/// </summary>
public sealed class OrderCreationFailedActivity : IStateMachineActivity<CheckoutSagaState, OrderFailedSagaEvent>
{
    private readonly ILogger<OrderCreationFailedActivity> _logger;

    public OrderCreationFailedActivity(ILogger<OrderCreationFailedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-creation-failed-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderFailedSagaEvent> context,
        IBehavior<CheckoutSagaState, OrderFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderCreationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
        }

        _logger.LogWarning(
            "{SagaType} {CorrelationId} order creation failed. ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderFailedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, OrderFailedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
