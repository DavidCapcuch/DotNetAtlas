using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Checkout-saga's variant of the payment-completed activity. Distinct from
/// PaymentProcessingSaga's <c>CaptureCompletedActivity</c> - that one tracks the inner
/// payment-saga capture; this one tracks the outer checkout-saga's view of the same event.
/// Drives the <c>AwaitingPayment -&gt; AwaitingConfirmation</c> transition per § 4.
/// </summary>
public sealed class
    PaymentCompletedCheckoutActivity : IStateMachineActivity<CheckoutSagaState, PaymentCompletedSagaEvent>
{
    private readonly ILogger<PaymentCompletedCheckoutActivity> _logger;

    public PaymentCompletedCheckoutActivity(ILogger<PaymentCompletedCheckoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("payment-completed-checkout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, PaymentCompletedSagaEvent> context,
        IBehavior<CheckoutSagaState, PaymentCompletedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(PaymentCompletedCheckoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(CheckoutSagaActivityTags.OrderId, saga.OrderId?.ToString() ?? string.Empty);
            activity.SetTag(SagaActivityTags.PaymentTransactionId, message.PaymentTransactionId.ToString());
        }

        if (saga.PaymentRequestedAtUtc is { } requested)
        {
            CheckoutSagaMetrics.RecordPaymentDuration(message.CompletedAtUtc - requested);
        }

        _logger.LogInformation(
            "{SagaType} {CorrelationId} payment captured. PaymentTransactionId: {PaymentTransactionId}, Amount: {Amount} {Currency}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.PaymentTransactionId, message.Amount,
            message.Currency);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, PaymentCompletedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, PaymentCompletedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
