using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Application.WeatherForecast.Services.Abstractions;

public interface IWeatherForecastProvider
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastCriteria criteria,
        CancellationToken ct);

    string Name { get; }
}
