using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Ordering confirms the order - drives the terminal
/// <c>AwaitingConfirmation -&gt; Confirmed</c> transition per § 4. Records confirmation
/// duration and total saga duration histograms per § 11.2.
/// </summary>
public sealed class OrderConfirmedActivity : IStateMachineActivity<CheckoutSagaState, OrderConfirmedSagaEvent>
{
    private readonly ILogger<OrderConfirmedActivity> _logger;

    public OrderConfirmedActivity(ILogger<OrderConfirmedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("order-confirmed-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, OrderConfirmedSagaEvent> context,
        IBehavior<CheckoutSagaState, OrderConfirmedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(OrderConfirmedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, message.OrderId.ToString());
        }

        if (saga.OrderConfirmationRequestedAtUtc is { } requested)
        {
            CheckoutSagaMetrics.RecordConfirmationDuration(message.ConfirmedAtUtc - requested);
        }

        CheckoutSagaMetrics.RecordConfirmed(message.ConfirmedAtUtc - saga.InitiatedAtUtc);
        CheckoutSagaMetrics.DecrementActive();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} confirmed. OrderId: {OrderId}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.OrderId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, OrderConfirmedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, OrderConfirmedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
