using Riok.Mapperly.Abstractions;
using Weather.Alerts;
using Weather.Domain.Alerts.Events;
using AvroSubscriptionTier = Weather.Alerts.SubscriptionTier;
using DomainSubscriptionTier = Weather.Domain.Alerts.ValueObjects.SubscriptionTier;

namespace Weather.Application.WeatherAlerts.PurchaseSubscription;

/// <summary>
/// Mapper for converting domain events to the SubscriptionActivatedEvent Avro integration event.
/// </summary>
[Mapper]
public static partial class SubscriptionActivatedMapper
{
    /// <summary>
    /// Maps a <see cref="SubscriberActivatedDomainEvent"/> to a <see cref="AlertSubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.Tier), nameof(AlertSubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.ExpiresAtUtc), nameof(AlertSubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriberActivatedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial AlertSubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriberActivatedDomainEvent source);

    /// <summary>
    /// Maps a <see cref="SubscriberReactivatedDomainEvent"/> to a <see cref="AlertSubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.Tier), nameof(AlertSubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.ExpiresAtUtc), nameof(AlertSubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriberReactivatedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial AlertSubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriberReactivatedDomainEvent source);

    /// <summary>
    /// Maps a <see cref="SubscriptionUpgradedDomainEvent"/> to a <see cref="AlertSubscriptionActivatedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.NewTier), nameof(AlertSubscriptionActivatedEvent.Tier))]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.ExpiresAtUtc), nameof(AlertSubscriptionActivatedEvent.ExpiresAtUtc))]
    [MapProperty(nameof(SubscriptionUpgradedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionActivatedEvent.ActivatedAtUtc))]
    public static partial AlertSubscriptionActivatedEvent ToSubscriptionActivatedEvent(this SubscriptionUpgradedDomainEvent source);

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
