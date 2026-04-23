using Ordering.Application.Orders.GetOrderById;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

/// <summary>
/// Paged envelope for a buyer's orders. Items reuse
/// <see cref="GetOrderByIdResponse"/> so summaries and detail queries never
/// drift.
/// </summary>
public sealed class GetOrdersByBuyerResponse
{
    public required IReadOnlyList<GetOrderByIdResponse> Orders { get; init; }

    public required int Skip { get; init; }

    public required int Take { get; init; }
}
