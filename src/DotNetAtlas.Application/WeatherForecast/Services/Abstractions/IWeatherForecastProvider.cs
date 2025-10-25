using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Requests;
using FluentResults;

namespace DotNetAtlas.Application.WeatherForecast.Services.Abstractions;

public interface IWeatherForecastProvider
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct);

    string Name { get; }
}
