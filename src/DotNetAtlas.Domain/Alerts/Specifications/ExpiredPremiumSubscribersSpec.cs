using Ardalis.Specification;
using DotNetAtlas.Domain.Alerts.ValueObjects;

namespace DotNetAtlas.Domain.Alerts.Specifications;

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
