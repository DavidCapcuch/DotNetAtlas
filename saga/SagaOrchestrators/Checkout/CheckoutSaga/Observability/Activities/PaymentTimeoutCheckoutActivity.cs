using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.Schedules;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Activity that fires when <c>PaymentTimeout</c> expires (transition
/// <c>AwaitingPayment -&gt; CompensatingStockReservations</c>) per
/// docs/bc-design/checkout-saga.md § 3. Increments <c>saga.checkout.payment_timeout</c>
/// counter. Named <c>PaymentTimeoutCheckoutActivity</c> to distinguish from any future
/// <c>PaymentProcessingSaga</c>-side payment-timeout activity.
/// </summary>
public sealed class PaymentTimeoutCheckoutActivity
    : IStateMachineActivity<CheckoutSagaState, PaymentTimeoutExpired>
{
    private readonly ILogger<PaymentTimeoutCheckoutActivity> _logger;

    public PaymentTimeoutCheckoutActivity(ILogger<PaymentTimeoutCheckoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("payment-timeout-checkout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, PaymentTimeoutExpired> context,
        IBehavior<CheckoutSagaState, PaymentTimeoutExpired> next)
    {
        var saga = context.Saga;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(PaymentTimeoutCheckoutActivity), saga.CorrelationId);
        if (activity?.IsAllDataRequested == true)
        {
            activity.SetTag(SagaActivityTags.UserId, saga.UserId.ToString());
            activity.SetTag(SagaActivityTags.ErrorCode, "PAYMENT_TIMEOUT");
        }

        CheckoutSagaMetrics.RecordPaymentTimeout();

        _logger.LogWarning(
            "{SagaType} {CorrelationId} payment timeout fired - PaymentCompletedEvent never arrived within budget",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, PaymentTimeoutExpired, TException> context,
        IBehavior<CheckoutSagaState, PaymentTimeoutExpired> next)
        where TException : Exception => next.Faulted(context);
}
