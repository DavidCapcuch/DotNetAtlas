using FastEndpoints;

namespace Ordering.API.Endpoints.Orders.MarkOrderShipped;

/// <summary>
/// HTTP request shape for <c>POST /api/v1/ordering/orders/{orderId}/ship</c>.
/// </summary>
public sealed class MarkOrderShippedRequest
{
    [RouteParam]
    public required Guid OrderId { get; init; }

    public required string Carrier { get; init; }

    public required string TrackingNumber { get; init; }
}
