using System.Diagnostics;
using FluentResults;
using Microsoft.Extensions.Logging;
using Platform.CQS;
using Weather.Application.Common.Observability.Tracing;
using Weather.Application.WeatherForecast.Common;
using Weather.Application.WeatherForecast.Services.Abstractions;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.ValueObjects;

namespace Weather.Application.WeatherForecast.GetForecasts;

public sealed class GetForecastQueryHandler : IQueryHandler<GetForecastQuery, GetForecastResponse>
{
    private readonly ILogger<GetForecastQueryHandler> _logger;
    private readonly IWeatherForecastService _weatherForecastService;
    private readonly IForecastEventsProducer _forecastEventsProducer;
    private readonly TimeProvider _timeProvider;

    public GetForecastQueryHandler(
        IWeatherForecastService weatherForecastService,
        IForecastEventsProducer forecastEventsProducer,
        ILogger<GetForecastQueryHandler> logger,
        TimeProvider timeProvider)
    {
        _weatherForecastService = weatherForecastService;
        _forecastEventsProducer = forecastEventsProducer;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<Result<GetForecastResponse>> HandleAsync(GetForecastQuery query, CancellationToken ct)
    {
        SetTraceTags(query);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var dateRangeResult = DateRange.Create(today, query.Days);
        if (dateRangeResult.IsFailed)
        {
            return Result.Fail(dateRangeResult.Errors);
        }

        var forecastCriteria = ForecastCriteria.Create(query.City, query.CountryCode, dateRangeResult.Value);
        if (forecastCriteria.IsFailed)
        {
            return Result.Fail(forecastCriteria.Errors);
        }

        var forecastResult = await _weatherForecastService.GetForecastAsync(forecastCriteria.Value, ct);
        if (forecastResult.IsFailed)
        {
            _logger.LogError("Failed to serve forecast for '{City},{CountryCode}'", query.City, query.CountryCode);
            return Result.Fail(forecastResult.Errors);
        }

        // Publish forecast requested event (fire-and-forget)
        // Note: This event is non-essential for the main operation flow.
        // We intentionally don't await to avoid blocking the response.
        // Exceptions are handled in the continuation to prevent affecting the main flow.
        PublishForecastRequestedEvent(forecastCriteria.Value, query.UserId);

        return new GetForecastResponse
        {
            Forecasts = [.. forecastResult.Value]
        };
    }

    private static void SetTraceTags(GetForecastQuery query)
    {
        Activity.Current?.SetTag(TraceTags.City, query.City);
        Activity.Current?.SetTag(TraceTags.CountryCode, query.CountryCode.ToString());
    }

    /// <summary>
    /// Publishes a forecast requested event in a fire-and-forget manner.
    /// This event is non-essential and should never block the main operation flow.
    /// Uses ContinueWith to ensure exceptions are handled even if the implementation
    /// changes to truly async in the future.
    /// </summary>
    private void PublishForecastRequestedEvent(ForecastCriteria forecastCriteria, Guid? userId)
    {
        try
        {
            _forecastEventsProducer.PublishForecastRequestedFireAndForgetAsync(forecastCriteria, userId)
                .ContinueWith(t =>
                    {
                        if (t.Exception != null)
                        {
                            _logger.LogError(t.Exception,
                                "Failed to publish forecast event for {City}",
                                forecastCriteria.City.Name);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

            _logger.LogDebug(
                "Queued ForecastRequestedEvent for {City}, {CountryCode}",
                forecastCriteria.City.Name, forecastCriteria.CountryCode);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log the error but don't fail the main flow
            _logger.LogError(
                ex, "Failed to queue ForecastRequestedEvent for {City}, {CountryCode}",
                forecastCriteria.City.Name, forecastCriteria.CountryCode);
        }
    }
}
