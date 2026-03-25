using FluentResults;
using Weather.Application.WeatherForecast.GetForecasts;
using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.Services.Abstractions;

public interface IWeatherForecastProvider
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastCriteria criteria,
        CancellationToken ct);

    string Name { get; }
}
