using FluentResults;
using Weather.Application.WeatherForecast.GetForecasts;
using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.Services.Abstractions;

public interface IWeatherForecastService
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(ForecastCriteria forecastCriteria, CancellationToken ct);
}
