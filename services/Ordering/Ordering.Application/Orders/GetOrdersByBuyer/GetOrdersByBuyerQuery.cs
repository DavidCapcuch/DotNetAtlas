using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Lists the caller's orders, most-recent-first, paged with offset / limit
/// (Appendix B.4 default). <c>Status</c> filter is optional.
/// </summary>
public sealed record GetOrdersByBuyerQuery : IQuery<GetOrdersByBuyerResponse>
{
    public required Guid BuyerId { get; init; }

    public string? Status { get; init; }

    public int Skip { get; init; }

    public int Take { get; init; } = 20;
}
