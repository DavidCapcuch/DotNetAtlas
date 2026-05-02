using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// MassTransit state machine implementing the Checkout saga - orchestrates the full
/// commercial-commitment flow across Basket -&gt; Ordering -&gt; Inventory -&gt; PaymentProcessingSaga
/// -&gt; Notifications. Eleven states including the abnormal-terminal CompensationStuck per
/// docs/bc-design/checkout-saga.md § 3.
/// </summary>
/// <remarks>
/// M1 + M2 scaffold - state declarations + internal saga events + correlation rules.
/// Schedules and Initially / During transitions land in M4 - M5.
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

    // Initiator event - missing-instance handling deferred to M4 Initially(...).
    public Event<BasketCheckoutInitiatedSagaEvent> BasketCheckoutInitiatedEvent { get; private set; }

    // Ordering events
    public Event<OrderCreatedSagaEvent> OrderCreatedEvent { get; private set; }
    public Event<OrderFailedSagaEvent> OrderFailedEvent { get; private set; }
    public Event<OrderCancelledSagaEvent> OrderCancelledEvent { get; private set; }
    public Event<OrderConfirmedSagaEvent> OrderConfirmedEvent { get; private set; }

    // Inventory events
    public Event<StockReservedSagaEvent> StockReservedEvent { get; private set; }
    public Event<StockReservationFailedSagaEvent> StockReservationFailedEvent { get; private set; }
    public Event<ReservationReleasedSagaEvent> ReservationReleasedEvent { get; private set; }
    public Event<ReservationConfirmedSagaEvent> ReservationConfirmedEvent { get; private set; }

    // Payments events (delegated via PaymentProcessingSaga)
    public Event<PaymentCompletedSagaEvent> PaymentCompletedEvent { get; private set; }
    public Event<PaymentFailedSagaEvent> PaymentFailedEvent { get; private set; }
    public Event<PaymentRefundedSagaEvent> PaymentRefundedEvent { get; private set; }

    public CheckoutSagaOrchestrator()
    {
        InstanceState(sagaState => sagaState.CurrentState);

        ConfigureEvents();
    }

    /// <summary>
    /// Wires correlation rules per docs/bc-design/checkout-saga.md § 4.1. Every event
    /// correlates by <c>CorrelationId</c>. All intermediate events use
    /// <c>OnMissingInstance(m =&gt; m.Discard())</c> so events arriving for an already-finalized
    /// (or out-of-order) saga are silently dropped - the spec-mandated divergence from
    /// PaymentProcessingSaga, which uses <c>Fault()</c> for some events. The initiator
    /// <see cref="BasketCheckoutInitiatedEvent"/> has no missing-instance policy because the M4
    /// <c>Initially(...)</c> handler creates the instance on first arrival.
    /// </summary>
    private void ConfigureEvents()
    {
        Event(() => BasketCheckoutInitiatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        Event(() => OrderCreatedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderCancelledEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => OrderConfirmedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => StockReservedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => StockReservationFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => ReservationReleasedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => ReservationConfirmedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentCompletedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentFailedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });

        Event(() => PaymentRefundedEvent, e =>
        {
            e.CorrelateById(ctx => ctx.Message.CorrelationId);
            e.OnMissingInstance(m => m.Discard());
        });
    }
}
