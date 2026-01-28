using DotNetAtlas.Domain.Alerts.Events;
using Riok.Mapperly.Abstractions;
using Weather.Alerts;

namespace DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;

/// <summary>
/// Mapper for converting domain events to the SubscriptionExtendedEvent Avro integration event.
/// </summary>
[Mapper]
public static partial class SubscriptionExtendedMapper
{
    /// <summary>
    /// Maps a <see cref="SubscriptionExtendedDomainEvent"/> to a <see cref="SubscriptionExtendedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.ExtendedByDays), nameof(SubscriptionExtendedEvent.DurationExtendedDays))]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.NewExpiresAtUtc), nameof(SubscriptionExtendedEvent.NewExpiresAtUtc))]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.OccurredOnUtc), nameof(SubscriptionExtendedEvent.ExtendedAtUtc))]
    public static partial SubscriptionExtendedEvent ToSubscriptionExtendedEvent(this SubscriptionExtendedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;
}

