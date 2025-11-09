using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using Riok.Mapperly.Abstractions;
using Weather.Forecast;

namespace DotNetAtlas.Infrastructure.Messaging.Kafka.DomainToAvroMappings;

[Mapper]
public static partial class ForecastEventsToAvroMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapValue(nameof(ForecastRequestedEvent.EventId), Use = nameof(GenerateEventId))]
    [MapValue(nameof(ForecastRequestedEvent.OccurredOnUtc), Use = nameof(GenerateOccurredOnUtc))]
    public static partial ForecastRequestedEvent ToForecastRequest(this GetForecastQuery source);

    private static Guid GenerateEventId() => Guid.CreateVersion7();
    private static DateTime GenerateOccurredOnUtc() => DateTime.UtcNow;
}
