using FastEndpoints;

namespace Ordering.API.Endpoints.Orders.MarkOrderDelivered;

/// <summary>
/// HTTP request shape for <c>POST /api/v1/ordering/orders/{orderId}/deliver</c>.
/// Body-less — only the route param is needed.
/// </summary>
public sealed class MarkOrderDeliveredRequest
{
    [RouteParam]
    public required Guid OrderId { get; init; }
}
