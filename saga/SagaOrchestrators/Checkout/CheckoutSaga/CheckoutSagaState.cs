using System.ComponentModel.DataAnnotations.Schema;
using Platform.SharedKernel.Base;
using SagaOrchestrators.Common.SagaAbstractions;

namespace SagaOrchestrators.Checkout.CheckoutSaga;

/// <summary>
/// Represents the state of the <see cref="CheckoutSagaOrchestrator"/>. The Checkout saga
/// turns a <c>BasketCheckoutInitiatedEvent</c> into either a confirmed order or a
/// fully-compensated rollback. The <see cref="CorrelationId"/> equals the basket's
/// pre-assigned <c>OrderId</c> (UUID v7) per ADR-0029 and threads through every downstream
/// command/event.
/// </summary>
public sealed class CheckoutSagaState : ISagaStateInstance, IAuditableEntity
{
    /// <summary>
    /// Uniquely identifies the saga instance. Equals BasketCheckoutInitiatedEvent.OrderId (ADR-0029).
    /// Immutable after first set.
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Current state of the saga state machine.
    /// </summary>
    public string CurrentState { get; set; } = ""; // always auto set by factory

    // — Buyer/user data (captured at init from BasketCheckoutInitiatedSagaEvent) —

    /// <summary>
    /// Identifier of the user initiating checkout. Becomes Ordering's BuyerId.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Sum of line totals as captured by Basket at checkout initiation.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// ISO 4217 currency code (e.g., 'USD', 'EUR').
    /// </summary>
    public string Currency { get; set; } = "";

    /// <summary>
    /// Saved payment method id — passed through to PaymentProcessingSaga and CreateOrderCommand
    /// (both of which still consume it as <c>Guid</c> in Wave-1). The boundary conversion to the
    /// gateway-token-shaped string happens at <c>BuildRequestPaymentCommand</c> when emitting the
    /// Payments-side wire command (C-2 closeout — Payments-side schema changed; Basket + Ordering
    /// wire shapes deferred).
    /// </summary>
    public Guid PaymentMethodId { get; set; }

    /// <summary>
    /// Serialized snapshot of basket line items (ProductId, Sku, Name, Quantity, UnitPriceAmount, LineTotal).
    /// Stored as a single jsonb column - the snapshot is immutable for the saga's lifetime.
    /// </summary>
    public string BasketSnapshotJson { get; set; } = "";

    /// <summary>
    /// Serialized shipping Address value object. Held only for the saga lifetime per ADR-0011 PII retention -
    /// nulled out on terminal Confirmed / Failed / Compensated / CompensationStuck transitions.
    /// </summary>
    public string? ShippingAddressJson { get; set; }

    /// <summary>
    /// Serialized billing Address value object. Same retention rule as <see cref="ShippingAddressJson"/>.
    /// </summary>
    public string? BillingAddressJson { get; set; }

    /// <summary>
    /// UTC timestamp when the saga was initiated (copied from the Basket event).
    /// </summary>
    public DateTimeOffset InitiatedAtUtc { get; set; }

    // — Ordering-side data (filled during AwaitingOrderCreation) —

    /// <summary>
    /// UTC timestamp when Ordering reported the order created.
    /// </summary>
    public DateTimeOffset? OrderCreatedAtUtc { get; set; }

    // — Inventory-side data (filled progressively during AwaitingStockReservation) —

    /// <summary>
    /// Serialized IDictionary&lt;Guid ProductId, ReservationTracking&gt; - one entry per distinct ProductId.
    /// ReservationTracking carries Status (Pending/Reserved/Failed/Released/Confirmed), ReservationId,
    /// ReservedAtUtc, ExpiresAtUtc. Updated on every Stock* / Reservation* saga event.
    /// </summary>
    public string ReservationIdsJson { get; set; } = "";

    /// <summary>
    /// Number of distinct ProductIds in the basket - target count for stock reservation completion.
    /// </summary>
    public int ExpectedReservations { get; set; }

    /// <summary>
    /// Decremented on each StockReservedSagaEvent. Zero triggers transition to AwaitingPayment.
    /// </summary>
    public int PendingReservations { get; set; }

    /// <summary>
    /// UTC timestamp when stock reservation fan-out began - for latency observability.
    /// </summary>
    public DateTimeOffset? StockReservationStartedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when all reservations completed (PendingReservations reached 0).
    /// </summary>
    public DateTimeOffset? StockReservationCompletedAtUtc { get; set; }

    // — Payment-side data (delegated to PaymentProcessingSaga) —

    /// <summary>
    /// Payment transaction id sourced from Payments' terminal <c>PaymentCompletedEvent</c> (set in
    /// <see cref="CheckoutSagaOrchestrator.AwaitingPaymentCapture"/> per ADR-0026); flowed onto the
    /// outbound <c>CheckoutCompletedEvent</c>.
    /// </summary>
    public Guid? PaymentTransactionId { get; set; }

    /// <summary>
    /// UTC timestamp when RequestPaymentCommand was emitted to payments.payment-commands (per ADR-0023).
    /// </summary>
    public DateTimeOffset? PaymentRequestedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when PaymentCompletedSagaEvent was received.
    /// </summary>
    public DateTimeOffset? PaymentCompletedAtUtc { get; set; }

    // — Confirmation timestamps —

    /// <summary>
    /// UTC timestamp when ConfirmOrderCommand + per-reservation ConfirmReservationCommands were dispatched.
    /// </summary>
    public DateTimeOffset? OrderConfirmationRequestedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when OrderConfirmedSagaEvent arrived from Ordering.
    /// </summary>
    public DateTimeOffset? OrderConfirmedAtUtc { get; set; }

    // — Compensation state —

    /// <summary>
    /// Number of in-flight reservations awaiting <c>ReservationReleasedSagaEvent</c> during
    /// compensation. Initialised on entry to <see cref="CheckoutSagaOrchestrator.CompensatingStockReservations"/>
    /// to the count of <c>Reserved</c> tracking entries; decremented as releases arrive. Zero AND
    /// <see cref="OrderCancelledReceived"/>=true is the gate for transition to <c>Compensated</c>
    /// per docs/bc-design/checkout-saga.md § 4 row 13.
    /// </summary>
    public int PendingReleases { get; set; }

    /// <summary>
    /// True once <c>OrderCancelledSagaEvent</c> has been observed during compensation. Together
    /// with <see cref="PendingReleases"/>=0 gates the transition to terminal <c>Compensated</c>.
    /// </summary>
    public bool OrderCancelledReceived { get; set; }

    /// <summary>
    /// UTC timestamp at first transition into any Compensating* state. Drives stuck-saga detection.
    /// </summary>
    public DateTimeOffset? CompensationStartedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp at transition into Compensated.
    /// </summary>
    public DateTimeOffset? CompensationCompletedAtUtc { get; set; }

    /// <summary>
    /// Set true on the first Compensating* transition - mirrors PaymentProcessingSagaState.CompensationTriggered.
    /// </summary>
    public bool CompensationTriggered { get; set; }

    /// <summary>
    /// Categorised failure code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED, ORDER_CREATION_TIMEOUT,
    /// CONFIRMATION_FAILED, COMPENSATION_TIMEOUT).
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Human-readable failure message - aids ops forensics + appears in CheckoutFailedEvent.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Name of the state when failure first occurred. Aids ops forensics.
    /// </summary>
    public string? FailedAtState { get; set; }

    // — Transient (in-memory only) state —

    /// <summary>
    /// Captured value of <see cref="CheckoutSagaFeatureFlags.PaymentThenStock"/> for the current
    /// consume of <c>OrderCreatedSagaEvent</c>. <see cref="NotMappedAttribute"/> — EF Core does not
    /// persist this; it is read once via <c>IFeatureClient</c> and consumed inside the same
    /// <c>IfElse</c> branch within one MassTransit transition. After <c>TransitionTo</c> we never
    /// need the value again, so non-persistence is safe.
    /// </summary>
    [NotMapped]
    public bool PaymentThenStockEnabled { get; set; }

    // — Timeout tokens (MassTransit scheduler) —

    public Guid? OrderCreationTimeoutTokenId { get; set; }
    public Guid? StockReservationTimeoutTokenId { get; set; }
    public Guid? PaymentTimeoutTokenId { get; set; }
    public Guid? OrderConfirmationTimeoutTokenId { get; set; }
    public Guid? CompensationTimeoutTokenId { get; set; }

    /// <summary>
    /// Optimistic concurrency token. EF Core IsRowVersion-mapped.
    /// </summary>
    public uint RowVersion { get; set; }

    // — Audit (IAuditableEntity, set by UpdateAuditableEntitiesInterceptor) —

    /// <summary>
    /// UTC timestamp when the saga row was created.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>
    /// UTC timestamp when the saga row was last mutated.
    /// </summary>
    public DateTimeOffset LastModifiedUtc { get; }

    /// <summary>
    /// Terminal states - sagas in these states are not considered "stuck" by the health check.
    /// CompensationStuck is terminal-but-abnormal: ops tracks it via a dedicated counter, not the
    /// stuck-saga gauge.
    /// </summary>
    public static readonly string[] TerminalStates =
    [
        nameof(CheckoutSagaOrchestrator.Confirmed),
        nameof(CheckoutSagaOrchestrator.Failed),
        nameof(CheckoutSagaOrchestrator.Compensated),
        nameof(CheckoutSagaOrchestrator.CompensationStuck)
    ];
}
