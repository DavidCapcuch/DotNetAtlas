using FastEndpoints;

namespace Ordering.API.Endpoints.Orders.CancelOrder;

/// <summary>
/// HTTP request shape for <c>POST /api/v1/ordering/orders/{orderId}/cancel</c>.
/// The <c>BuyerId</c> on the underlying
/// <c>Ordering.Application.Orders.CancelOrder.CancelOrderCommand</c> is NOT
/// taken from the body — it is derived from the JWT <c>sub</c> claim by the
/// endpoint, so a buyer cannot impersonate another buyer through the body.
/// </summary>
public sealed class CancelOrderRequest
{
    [RouteParam]
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Free-text cancellation reason. 1–500 characters per the application
    /// validator (<c>Ordering.Application.Orders.CancelOrder.CancelOrderCommandValidator</c>).
    /// </summary>
    public required string Reason { get; init; }
}
