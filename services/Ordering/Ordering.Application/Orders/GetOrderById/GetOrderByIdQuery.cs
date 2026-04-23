using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrderById;

/// <summary>
/// Reads a single order by id. Authorization: buyer sees only own; admin
/// sees any. Buyer-asks-for-another's-order resolves to NotFound (do NOT
/// leak existence).
/// </summary>
public sealed class GetOrderByIdQuery : IQuery<GetOrderByIdResponse>
{
    public required Guid OrderId { get; init; }

    public required Guid BuyerId { get; init; }

    public required bool IsAdmin { get; init; }
}
