using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.Common;

public interface IForecastEventsProducer
{
    /// <summary>
    /// Publishes a forecast request event.
    /// </summary>
    /// <param name="forecastCriteria">The forecast criteria containing city, country, and date range.</param>
    /// <param name="userId">Optional user identifier who issued the request.</param>
    Task PublishForecastRequestedFireAndForgetAsync(ForecastCriteria forecastCriteria, Guid? userId);
}
