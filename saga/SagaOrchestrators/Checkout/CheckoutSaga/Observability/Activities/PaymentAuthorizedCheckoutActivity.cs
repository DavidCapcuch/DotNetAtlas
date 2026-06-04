using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability.Activities;

/// <summary>
/// Checkout-saga activity for the payment-authorized event (ADR-0026 capture pivot). Drives the
/// <c>AwaitingPaymentAuthorization -&gt; AwaitingConfirmation</c> transition where the Checkout
/// saga confirms stock + order before approving capture. Distinct from PaymentProcessingSaga's
/// <c>AuthorizationCompletedActivity</c> — that one tracks the inner payment-saga authorization;
/// this one tracks the outer checkout-saga's view of the same event.
/// </summary>
public sealed class
    PaymentAuthorizedCheckoutActivity : IStateMachineActivity<CheckoutSagaState, PaymentAuthorizedCheckoutSagaEvent>
{
    private readonly ILogger<PaymentAuthorizedCheckoutActivity> _logger;

    public PaymentAuthorizedCheckoutActivity(ILogger<PaymentAuthorizedCheckoutActivity> logger)
    {
        _logger = logger;
    }

    public void Probe(ProbeContext context) => context.CreateScope("payment-authorized-checkout-activity");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(
        BehaviorContext<CheckoutSagaState, PaymentAuthorizedCheckoutSagaEvent> context,
        IBehavior<CheckoutSagaState, PaymentAuthorizedCheckoutSagaEvent> next)
    {
        var saga = context.Saga;
        var message = context.Message;

        using var activity =
            CheckoutSagaActivitySource.StartActivity(nameof(PaymentAuthorizedCheckoutActivity), saga.CorrelationId);

        _logger.LogInformation(
            "{SagaType} {CorrelationId} payment authorized - confirming order + reservations before capture. AuthorizationId: {AuthorizationId}",
            nameof(CheckoutSagaOrchestrator), saga.CorrelationId, message.AuthorizationId);

        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<CheckoutSagaState, PaymentAuthorizedCheckoutSagaEvent, TException> context,
        IBehavior<CheckoutSagaState, PaymentAuthorizedCheckoutSagaEvent> next)
        where TException : Exception => next.Faulted(context);
}
