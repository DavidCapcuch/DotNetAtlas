using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;

namespace DotNetAtlas.Application.Forecast.Services.Abstractions;

public interface IWeatherForecastProvider
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct);

    string Name { get; }
}
