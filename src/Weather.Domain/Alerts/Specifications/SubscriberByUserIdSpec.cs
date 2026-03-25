using Ardalis.Specification;

namespace Weather.Domain.Alerts.Specifications;

/// <summary>
/// Specification to find a subscriber by user ID with their subscriptions loaded.
/// </summary>
public sealed class SubscriberByUserIdSpec : Specification<AlertSubscriber>,
    ISingleResultSpecification<AlertSubscriber>
{
    public SubscriberByUserIdSpec(Guid userId)
    {
        Query
            .Where(s => s.UserId == userId)
            .Include(s => s.MonitoredLocationAlertsSubscriptions)
            .TagWith(nameof(SubscriberByUserIdSpec));
    }
}
