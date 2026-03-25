using Riok.Mapperly.Abstractions;
using Weather.Alerts;
using Weather.Domain.Alerts.Events;

namespace Weather.Application.WeatherAlerts.ExtendSubscription;

/// <summary>
/// Mapper for converting domain events to the SubscriptionExtendedEvent Avro integration event.
/// </summary>
[Mapper]
public static partial class SubscriptionExtendedMapper
{
    /// <summary>
    /// Maps a <see cref="SubscriptionExtendedDomainEvent"/> to a <see cref="AlertSubscriptionExtendedEvent"/>.
    /// </summary>
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.ExtendedByDays), nameof(AlertSubscriptionExtendedEvent.DurationExtendedDays))]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.NewExpiresAtUtc), nameof(AlertSubscriptionExtendedEvent.NewExpiresAtUtc))]
    [MapProperty(nameof(SubscriptionExtendedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionExtendedEvent.ExtendedAtUtc))]
    public static partial AlertSubscriptionExtendedEvent ToSubscriptionExtendedEvent(this SubscriptionExtendedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;
}
