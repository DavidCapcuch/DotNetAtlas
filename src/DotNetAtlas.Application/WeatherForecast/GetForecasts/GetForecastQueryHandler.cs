using System.Diagnostics;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Application.WeatherForecast.Common.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.Application.WeatherForecast.GetForecasts;

public class GetForecastQueryHandler : IQueryHandler<GetForecastQuery, GetForecastResponse>
{
    private readonly ILogger<GetForecastQueryHandler> _logger;
    private readonly IWeatherForecastService _forecastService;
    private readonly IForecastEventsProducer _forecastEventsProducer;

    public GetForecastQueryHandler(
        IWeatherForecastService forecastService,
        ILogger<GetForecastQueryHandler> logger,
        IForecastEventsProducer eventsPublisher)
    {
        _forecastService = forecastService;
        _logger = logger;
        _forecastEventsProducer = eventsPublisher;
    }

    public async Task<Result<GetForecastResponse>> HandleAsync(GetForecastQuery query, CancellationToken ct)
    {
        Activity.Current?.SetTag(DiagnosticNames.City, query.City);
        Activity.Current?.SetTag(DiagnosticNames.CountryCode, query.CountryCode.ToString());

        _ = Task.Run(async () =>
        {
            try
            {
                await _forecastEventsProducer.PublishForecastRequestedAsync(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish forecast request");
            }
        }, ct);

        var forecastRequest = query.ToForecastRequest();
        var forecastResult = await _forecastService.GetForecastAsync(forecastRequest, ct);
        if (forecastResult.IsFailed)
        {
            _logger.LogError("Failed to serve forecast for '{City},{CountryCode}'", query.City, query.CountryCode);

            return Result.Fail(forecastResult.Errors);
        }

        return new GetForecastResponse
        {
            Forecasts = forecastResult.Value
        };
    }
}
