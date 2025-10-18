using DotNetAtlas.Application.Forecast.GetForecasts;

namespace DotNetAtlas.Application.Forecast.Common.Abstractions;

public interface IForecastEventsProducer
{
    Task PublishForecastRequestedAsync(GetForecastQuery message, CancellationToken ct);
}
