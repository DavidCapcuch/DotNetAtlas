using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Payments acknowledges the refund command - drives the second
/// phase of the AwaitingConfirmation-failure compensation: <c>CompensatingPayment -&gt;
/// CompensatingStockReservations</c> per § 4 row 16 + § 6.1 two-phase rationale.
/// </summary>
public sealed class PaymentRefundedActivity : IStateMachineActivity<CheckoutSagaState, PaymentRefundedSagaEvent>
{
    private readonly ILogger<PaymentRefundedActivity> _logger;

    public PaymentRefundedActivity(ILogger<PaymentRefundedActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("payment-refunded-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, PaymentRefundedSagaEvent> context,
        IBehavior<CheckoutSagaState, PaymentRefundedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(PaymentRefundedActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, saga.OrderId?.ToString() ?? string.Empty);
            activity.SetTag(SagaActivityTags.PaymentTransactionId, message.PaymentTransactionId.ToString());
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} payment refunded - releasing stock to complete compensation. PaymentTransactionId: {PaymentTransactionId}, Amount: {Amount} {Currency}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.PaymentTransactionId, message.Amount,
            message.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, PaymentRefundedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, PaymentRefundedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
