using Ardalis.Specification;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.Domain.Alerts.Specifications;

/// <summary>
/// Specification to find premium subscribers whose subscription has expired.
/// </summary>
public sealed class ExpiredPremiumSubscribersSpec : Specification<AlertSubscriber>
{
    public ExpiredPremiumSubscribersSpec(DateTimeOffset currentUtc)
    {
        Query
            .Where(s => s.SubscriptionTier != SubscriptionTier.Free
                        && s.SubscriptionExpiryAtUtc != null
                        && s.SubscriptionExpiryAtUtc <= currentUtc)
            .TagWith(nameof(ExpiredPremiumSubscribersSpec));
    }
}
