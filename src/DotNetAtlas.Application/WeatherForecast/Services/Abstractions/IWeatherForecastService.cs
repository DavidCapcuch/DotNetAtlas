using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Application.WeatherForecast.Services.Abstractions;

public interface IWeatherForecastService
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(ForecastCriteria forecastCriteria, CancellationToken ct);
}
