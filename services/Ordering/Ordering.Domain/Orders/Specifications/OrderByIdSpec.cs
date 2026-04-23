using Ardalis.Specification;

namespace Ordering.Domain.Orders.Specifications;

/// <summary>
/// Loads a single <see cref="Order"/> by its primary key.
/// Used by every saga-command handler and the <c>GetOrderByIdQuery</c> — any
/// consumer that needs the aggregate by id.
/// </summary>
/// <remarks>
/// Tagged with the spec class name for SQL-level observability (EF Core
/// emits the tag as a comment in the generated query).
/// </remarks>
public sealed class OrderByIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByIdSpec(Guid orderId)
    {
        Query
            .Where(o => o.Id == orderId)
            .TagWith(nameof(OrderByIdSpec));
    }
}
