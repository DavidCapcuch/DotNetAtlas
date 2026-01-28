using DotNetAtlas.Domain.Forecast.ValueObjects;
using Riok.Mapperly.Abstractions;
using Weather.Forecast;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.WeatherForecastEvents;

/// <summary>
/// Mapper for converting domain forecast criteria to Avro ForecastRequestedEvent.
/// Uses Mapperly source generator for compile-time mapping.
/// </summary>
[Mapper(
    RequiredMappingStrategy = RequiredMappingStrategy.Target,
    EnumMappingStrategy = EnumMappingStrategy.ByName)]
public static partial class ForecastRequestedEventMapper
{
    [MapProperty(nameof(@ForecastCriteria.City.Name), nameof(ForecastRequestedEvent.City))]
    [MapProperty(nameof(@ForecastCriteria.DateRange.StartDateOnly), nameof(ForecastRequestedEvent.StartDateLocal), Use = nameof(DateOnlyToDateTime))]
    [MapProperty(nameof(@ForecastCriteria.DateRange.EndDateOnly), nameof(ForecastRequestedEvent.EndDateLocal), Use = nameof(DateOnlyToDateTime))]
    public static partial ForecastRequestedEvent ToForecastRequestedEvent(
        this ForecastCriteria source,
        Guid? userId,
        DateTime occurredOnUtc);

    [UserMapping]
    private static DateTime DateOnlyToDateTime(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
}
