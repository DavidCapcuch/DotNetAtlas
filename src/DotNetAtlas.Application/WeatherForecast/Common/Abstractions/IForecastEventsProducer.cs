using DotNetAtlas.Application.WeatherForecast.GetForecasts;

namespace DotNetAtlas.Application.WeatherForecast.Common.Abstractions;

public interface IForecastEventsProducer
{
    Task PublishForecastRequestedAsync(GetForecastQuery message, CancellationToken ct);
}
