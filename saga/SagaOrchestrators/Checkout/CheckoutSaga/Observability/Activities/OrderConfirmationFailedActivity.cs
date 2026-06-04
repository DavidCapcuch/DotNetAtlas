using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Ordering rejects the confirm-order command (transition
/// <c>AwaitingConfirmation -&gt; CompensatingPayment</c>) per § 4 transition table.
/// Distinct from <c>OrderCreationFailedActivity</c> even though both bind to
/// <see cref="OrderFailedSagaEvent"/> - the orchestrator selects which activity runs via
/// <c>.OfType&lt;...&gt;()</c> at the call site.
/// </summary>
public sealed class
    OrderConfirmationFailedActivity : IStateMachineActivity<CheckoutSagaState, OrderFailedSagaEvent>
{
    private readonly ILogger<OrderConfirmationFailedActivity> _logger;

    public OrderConfirmationFailedActivity(ILogger<OrderConfirmationFailedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-confirmation-failed-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderFailedSagaEvent> context,
        IBehavior<CheckoutSagaState, OrderFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderConfirmationFailedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
        }

        _logger.LogWarning(
            "{SagaType} {CorrelationId} order confirmation failed - refund-first compensation. ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderFailedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, OrderFailedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
