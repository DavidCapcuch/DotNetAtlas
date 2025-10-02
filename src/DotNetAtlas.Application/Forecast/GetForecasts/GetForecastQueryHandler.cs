using System.Diagnostics;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.Forecast.GetForecasts;

public class GetForecastQueryHandler : IQueryHandler<GetForecastQuery, GetForecastResponse>
{
    private readonly ILogger<GetForecastQueryHandler> _logger;
    private readonly IWeatherForecastService _forecastService;

    public GetForecastQueryHandler(IWeatherForecastService forecastService, ILogger<GetForecastQueryHandler> logger)
    {
        _forecastService = forecastService;
        _logger = logger;
    }

    public async Task<Result<GetForecastResponse>> HandleAsync(GetForecastQuery query, CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.City, query.City);
        Activity.Current?.SetTag(DiagnosticNames.CountryCode, query.CountryCode.ToString());

        var forecastRequest = query.ToForecastRequest();
        var result = await _forecastService.GetForecastAsync(forecastRequest, ct);
        if (result.IsFailed)
        {
            _logger.LogError("Failed to serve forecast for '{City},{CountryCode}'", query.City, query.CountryCode);

            return Result.Fail(result.Errors);
        }

        return new GetForecastResponse
        {
            Forecasts = result.Value
        };
    }
}
