using DotNetAtlas.Domain.Alerts.Events;
using Riok.Mapperly.Abstractions;
using Weather.Alerts;
using AvroSubscriptionTier = Weather.Alerts.SubscriptionTier;
using DomainSubscriptionTier = DotNetAtlas.Domain.Alerts.ValueObjects.SubscriptionTier;

namespace DotNetAtlas.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Mapper for converting domain events to the SubscriptionActivatedEvent Avro integration event.
/// </summary>
[Mapper]
public static partial class SubscriptionActivatedMapper
{
    /// <summary>
    /// Maps a <see cref="SubscriberActivatedDomainEvent"/> to a <see cref="SubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.Tier), nameof(SubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.ExpiresAtUtc), nameof(SubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.OccurredOnUtc), nameof(SubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial SubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriberActivatedDomainEvent source);

    /// <summary>
    /// Maps a <see cref="SubscriberReactivatedDomainEvent"/> to a <see cref="SubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.Tier), nameof(SubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.ExpiresAtUtc), nameof(SubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.OccurredOnUtc), nameof(SubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial SubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriberReactivatedDomainEvent source);

    /// <summary>
    /// Maps a <see cref="SubscriptionUpgradedDomainEvent"/> to a <see cref="SubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.NewTier), nameof(SubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.ExpiresAtUtc), nameof(SubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.OccurredOnUtc), nameof(SubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial SubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriptionUpgradedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;

    [UserMapping]
    private static AvroSubscriptionTier MapSubscriptionTier(DomainSubscriptionTier tier) =>
        tier.Name switch
        {
            nameof(DomainSubscriptionTier.Pro) => AvroSubscriptionTier.Pro,
            nameof(DomainSubscriptionTier.Ultra) => AvroSubscriptionTier.Ultra,
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown subscription tier")
        };
}
