using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Lists the caller's orders, most-recent-first, paged with 1-indexed
/// <c>pageNumber</c> / <c>pageSize</c> (<c>use-cases.md § 3.4.2</c>). The
/// <c>Status</c> filter is optional.
/// </summary>
public sealed record GetOrdersByBuyerQuery : IQuery<GetOrdersByBuyerResponse>
{
    public required Guid BuyerId { get; init; }

    public string? Status { get; init; }

    public required int PageNumber { get; init; }

    public required int PageSize { get; init; }
}
