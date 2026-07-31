using System.ComponentModel;

namespace Ordering.Api.Endpoints.Orders.GetOrdersByBuyer;

/// <summary>
/// HTTP request shape for <c>GET /api/v1/ordering/orders</c>. v1 lists only
/// the caller's own orders — the admin override <c>?buyerId=</c> is
/// deferred to v2+ per <c>ordering.md Appendix B</c>.
/// </summary>
public sealed class GetOrdersByBuyerRequest
{
    /// <summary>Page served when the caller omits <c>pageNumber</c>.</summary>
    public const int DefaultPageNumber = 1;

    /// <summary>Page size served when the caller omits <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Optional <c>OrderStatus</c> filter (e.g. <c>Created</c>, <c>Shipped</c>).
    /// Validated by <c>Ordering.Application.Orders.GetOrdersByBuyer.GetOrdersByBuyerQueryValidator</c>.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 1-indexed page. Nullable because it is genuinely optional: the generated OpenAPI
    /// document derives parameter requiredness from nullability, so a non-nullable member
    /// would publish this as mandatory while the endpoint supplies
    /// <see cref="DefaultPageNumber"/> for callers who omit it.
    /// </summary>
    [DefaultValue(DefaultPageNumber)]
    public int? PageNumber { get; init; }

    /// <summary>Nullable for the same reason as <see cref="PageNumber"/>.</summary>
    [DefaultValue(DefaultPageSize)]
    public int? PageSize { get; init; }
}
