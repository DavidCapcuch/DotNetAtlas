using FastEndpoints;

namespace Ordering.API.Endpoints.Orders.GetOrderById;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/ordering/orders/{orderId}</c>.
/// </summary>
public sealed class GetOrderByIdRequest
{
    [RouteParam]
    public required Guid OrderId { get; init; }
}
