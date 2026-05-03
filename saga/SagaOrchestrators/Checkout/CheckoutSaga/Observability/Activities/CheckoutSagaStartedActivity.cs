using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that records metrics, traces, and logs when the
/// <see cref="CheckoutSagaOrchestrator"/> starts processing a new checkout per
/// docs/bc-design/checkout-saga.md § 11.1.
/// </summary>
public sealed class
    CheckoutSagaStartedActivity : IStateMachineActivity<CheckoutSagaState, BasketCheckoutInitiatedSagaEvent>
{
    private readonly ILogger<CheckoutSagaStartedActivity> _logger;

    public CheckoutSagaStartedActivity(ILogger<CheckoutSagaStartedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("checkout-saga-started-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, BasketCheckoutInitiatedSagaEvent> context,
        IBehavior<CheckoutSagaState, BasketCheckoutInitiatedSagaEvent> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(CheckoutSagaStartedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
        }

        CheckoutSagaMetrics.RecordInitiated();
        CheckoutSagaMetrics.IncrementActive();

        _logger.LogInformation(
            "{SagaType} {CorrelationId} initiated for user {UserId}, total {TotalAmount} {Currency}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, saga.UserId, saga.TotalAmount, saga.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, BasketCheckoutInitiatedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, BasketCheckoutInitiatedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
