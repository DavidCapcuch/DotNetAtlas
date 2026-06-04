using SagaOrchestrators.Common.Observability.Tracing;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Observability;

/// <summary>
/// Activity tags specific to the Checkout saga. For common tags (CorrelationId, UserId,
/// ErrorCode, ErrorMessage, PaymentTransactionId), see <see cref="SagaActivityTags"/>.
/// </summary>
public static class CheckoutSagaActivityTags
{
    /// <summary>
    /// A specific Inventory product id - tagged on per-line StockReserved / ReservationReleased / ReservationConfirmed activities.
    /// </summary>
    public const string ProductId = "saga.product_id";

    /// <summary>
    /// A specific reservation id - present on per-line activities once the reservation is created.
    /// </summary>
    public const string ReservationId = "saga.reservation_id";

    /// <summary>
    /// Number of distinct ProductIds in the basket - tagged on the AllStockReserved transition activity.
    /// </summary>
    public const string ExpectedReservations = "saga.expected_reservations";

    /// <summary>
    /// Count of reservations still awaiting Inventory's StockReservedEvent - tagged during fan-in.
    /// </summary>
    public const string PendingReservations = "saga.pending_reservations";

    /// <summary>
    /// Name of the Compensating* state when CompensationStuck fires - aids ops forensics on the runbook.
    /// </summary>
    public const string LastState = "saga.last_state";
}
