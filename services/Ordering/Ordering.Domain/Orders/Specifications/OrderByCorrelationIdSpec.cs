using Ardalis.Specification;

namespace Ordering.Domain.Orders.Specifications;

/// <summary>
/// Finds the <see cref="Order"/> created for a given saga correlation id —
/// used by <c>CreateOrderCommandHandler</c> to make the command idempotent
/// under Kafka redelivery (see <c>use-cases.md § 3.1.1</c> step 1).
/// </summary>
/// <remarks>
/// <see cref="Order.CorrelationId"/> is assigned at factory time and is
/// immutable thereafter (I-3..I-5 in <c>ordering.md § 3.1</c>), so a hit on
/// this spec is a definitive "already created" signal.
/// </remarks>
public sealed class OrderByCorrelationIdSpec : Specification<Order>, ISingleResultSpecification<Order>
{
    public OrderByCorrelationIdSpec(Guid correlationId)
    {
        Query
            .Where(o => o.CorrelationId == correlationId)
            .TagWith(nameof(OrderByCorrelationIdSpec));
    }
}
