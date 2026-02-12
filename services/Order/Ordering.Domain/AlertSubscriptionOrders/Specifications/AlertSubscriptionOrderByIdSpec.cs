using Ardalis.Specification;

namespace Ordering.Domain.AlertSubscriptionOrders.Specifications;

public sealed class AlertSubscriptionOrderByIdSpec : Specification<AlertSubscriptionOrder>
{
    public AlertSubscriptionOrderByIdSpec(Guid id)
    {
        Query
            .Where(o => o.Id == id)
            .TagWith(nameof(AlertSubscriptionOrderByIdSpec));
    }
}
