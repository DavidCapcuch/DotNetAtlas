using Ardalis.Specification;

namespace Ordering.Domain.Orders.Specifications;

/// <summary>
/// Loads a single <see cref="Order"/> by its primary key. Write-side only —
/// used by every saga-command handler that needs to mutate the aggregate.
/// Per ADR-0021 the CQRS read side does not consume this spec; query
/// handlers use inline LINQ with SQL-side projection instead.
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
