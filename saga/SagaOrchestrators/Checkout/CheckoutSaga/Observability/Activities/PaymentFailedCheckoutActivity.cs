using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when Payments reports payment failure - drives the
/// <c>AwaitingPayment -&gt; CompensatingStockReservations</c> transition per § 4. No refund
/// needed (payment never captured); compensation is stock-release + order-cancel only.
/// </summary>
public sealed class
    PaymentFailedCheckoutActivity : IStateMachineActivity<CheckoutSagaState, PaymentFailedSagaEvent>
{
    private readonly ILogger<PaymentFailedCheckoutActivity> _logger;

    public PaymentFailedCheckoutActivity(ILogger<PaymentFailedCheckoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("payment-failed-checkout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, PaymentFailedSagaEvent> context,
        IBehavior<CheckoutSagaState, PaymentFailedSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(PaymentFailedCheckoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.ErrorCode, message.ErrorCode);
        }

        CheckoutSagaMetrics.RecordPaymentFailed(message.ErrorCode);

        _logger.LogWarning(
            "{SagaType} {CorrelationId} payment failed. ErrorCode: {ErrorCode}, ErrorMessage: {ErrorMessage}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.ErrorCode, message.ErrorMessage);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, PaymentFailedSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, PaymentFailedSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
