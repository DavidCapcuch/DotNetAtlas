using MassTransit;

namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// MassTransit state machine implementing the Checkout saga - orchestrates the full
/// commercial-commitment flow across Basket -&gt; Ordering -&gt; Inventory -&gt; PaymentProcessingSaga
/// -&gt; Notifications. Eleven states including the abnormal-terminal CompensationStuck per
/// docs/bc-design/checkout-saga.md § 3.
/// </summary>
/// <remarks>
/// M1 scaffold - state declarations only. Events, schedules, and Initially / During transitions
/// land in M2 - M5.
/// </remarks>
public sealed class CheckoutSagaOrchestrator : MassTransitStateMachine<CheckoutSagaState>
{
    // Happy path states (Initial is MassTransit-implicit)
    public State AwaitingOrderCreation { get; private set; }
    public State AwaitingStockReservation { get; private set; }
    public State AwaitingPayment { get; private set; }
    public State AwaitingConfirmation { get; private set; }
    public State Confirmed { get; private set; }

    // Compensation states
    public State CompensatingStockReservations { get; private set; }
    public State CompensatingPayment { get; private set; }
    public State Compensated { get; private set; }
    public State Failed { get; private set; }
    public State CompensationStuck { get; private set; }

    public CheckoutSagaOrchestrator()
    {
        InstanceState(sagaState => sagaState.CurrentState);
    }
}
