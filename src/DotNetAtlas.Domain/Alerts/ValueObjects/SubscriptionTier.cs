using Ardalis.SmartEnum;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Smart enum representing subscription tier type for users.
/// Encapsulates tier identity and associated business rules.
/// </summary>
public sealed class SubscriptionTier : SmartEnum<SubscriptionTier>
{
    public static readonly SubscriptionTier Free = new(nameof(Free), 0, maxSubscriptions: 5);
    public static readonly SubscriptionTier Pro = new(nameof(Pro), 1, maxSubscriptions: 25);
    public static readonly SubscriptionTier Ultra = new(nameof(Ultra), 2, maxSubscriptions: 100);

    public int MaxSubscriptions { get; }

    private SubscriptionTier(string name, int value, int maxSubscriptions)
        : base(name, value)
    {
        MaxSubscriptions = maxSubscriptions;
    }
}
