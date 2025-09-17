using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;

namespace DotNetAtlas.Application.Forecast.Services.Abstractions;

public interface IWeatherForecastService
{
    Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(ForecastRequest forecastRequest, CancellationToken ct);
}
