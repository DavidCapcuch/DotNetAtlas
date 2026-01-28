using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Services.Config;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Application.WeatherForecast.Services;

public class HedgingWeatherForecastService : IWeatherForecastService
{
    private readonly List<IWeatherForecastProvider> _weatherForecastProviders;
    private readonly IMainWeatherForecastProvider _mainWeatherForecastProvider;
    private readonly ILogger<HedgingWeatherForecastService> _logger;
    private readonly WeatherHedgingOptions _hedgingOptions;

    public HedgingWeatherForecastService(
        IMainWeatherForecastProvider mainWeatherForecastProvider,
        IEnumerable<IWeatherForecastProvider> weatherProviders,
        ILogger<HedgingWeatherForecastService> logger,
        IOptions<WeatherHedgingOptions> hedgingOptions)
    {
        _weatherForecastProviders = [.. weatherProviders];
        _logger = logger;
        _mainWeatherForecastProvider = mainWeatherForecastProvider;
        _hedgingOptions = hedgingOptions.Value;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastCriteria forecastCriteria,
        CancellationToken ct)
    {
        // Try only the primary provider first
        using var primaryProviderCallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        primaryProviderCallCts.CancelAfter(_hedgingOptions.PrimaryMaxDurationMs);
        try
        {
            var primaryResult =
                await _mainWeatherForecastProvider.GetForecastAsync(forecastCriteria, primaryProviderCallCts.Token);

            return primaryResult;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Primary weather provider {ProviderName} timeout", _mainWeatherForecastProvider.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Primary weather provider {ProviderName} failed, hedging across others",
                _mainWeatherForecastProvider.Name);
        }

        // Try all providers concurrently as fallback
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var getForecastTasks = _weatherForecastProviders
            .Select(provider => provider.GetForecastAsync(forecastCriteria, cts.Token))
            .ToList();

        var exceptions = new List<Exception>();
        await foreach (var getForecastTask in Task.WhenEach(getForecastTasks).WithCancellation(cts.Token))
        {
            try
            {
                var forecastResult = await getForecastTask;

                if (forecastResult.IsSuccess)
                {
                    // Cancel other weather provider api calls
                    await cts.CancelAsync();

                    return forecastResult;
                }

                var codes = forecastResult.Errors.ToErrorsSummary();
                _logger.LogWarning("Hedged weather provider call failed, errors: {Codes}", codes);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Expected cancellation from hedging
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Hedged weather provider call failed");
                exceptions.Add(ex);
            }
        }

        throw new AggregateException("All weather providers failed.", exceptions);
    }
}
