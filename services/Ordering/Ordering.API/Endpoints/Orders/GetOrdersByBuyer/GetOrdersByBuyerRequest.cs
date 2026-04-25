namespace Ordering.API.Endpoints.Orders.GetOrdersByBuyer;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/ordering/orders</c>. v1 lists only
/// the caller's own orders — the admin override <c>?buyerId=</c> is
/// deferred to v2+ per <c>ordering.md Appendix B</c>.
/// </summary>
public sealed class GetOrdersByBuyerRequest
{
    /// <summary>
    /// Optional <c>OrderStatus</c> filter (e.g. <c>Created</c>, <c>Shipped</c>).
    /// Validated by <c>Ordering.Application.Orders.GetOrdersByBuyer.GetOrdersByBuyerQueryValidator</c>.
    /// </summary>
    public string? Status { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}
