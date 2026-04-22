using Platform.SharedKernel.Errors;

namespace Ordering.Domain.Errors;

/// <summary>
/// Single-source-of-truth factories for Ordering user-visible errors.
/// Scope intentionally narrow: FSM-transition bugs throw <c>DataIntegrityException</c>
/// (they are caller bugs, not user errors) — see <c>Order.MarkStockReserved</c> etc.
/// Only truly user-actionable conditions surface as <see cref="ValidationError"/>.
/// </summary>
/// <remarks>
/// Names and factory shapes are locked by
/// <c>docs/bc-design/error-taxonomy.md § 3.3</c>.
/// </remarks>
public static class OrderingErrors
{
    /// <summary>
    /// Returned by <c>Order.Cancel</c> when the current status does not allow
    /// cancellation (i.e. <c>Shipped</c>, <c>Delivered</c>, or any terminal
    /// state). Maps to <c>409 Conflict</c> at the HTTP surface (I-12).
    /// </summary>
    public static ValidationError CannotCancelInStatus(string status) =>
        new(
            propertyName: "Status",
            errorMessage: $"Order in status '{status}' cannot be cancelled.",
            errorCode: "Order.CannotCancelInStatus");

    /// <summary>
    /// Returned by read-side query handlers when no order matches the supplied id.
    /// Maps to <c>404 Not Found</c> at the HTTP surface.
    /// </summary>
    public static ValidationError OrderNotFound(Guid orderId) =>
        new(
            propertyName: "OrderId",
            errorMessage: $"Order '{orderId}' does not exist.",
            errorCode: "Order.NotFound");
}
