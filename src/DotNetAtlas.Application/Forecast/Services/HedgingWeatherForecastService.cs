using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Application.Forecast.Services;

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
        _weatherForecastProviders = weatherProviders.ToList();
        _logger = logger;
        _mainWeatherForecastProvider = mainWeatherForecastProvider;
        _hedgingOptions = hedgingOptions.Value;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct)
    {
        // Try only the primary provider first
        using var primaryProviderCallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        primaryProviderCallCts.CancelAfter(_hedgingOptions.PrimaryMaxDurationMs);
        try
        {
            var primaryResult =
                await _mainWeatherForecastProvider.GetForecastAsync(forecastRequest, primaryProviderCallCts.Token);

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
            .Select(provider => provider.GetForecastAsync(forecastRequest, cts.Token))
            .ToList();

        var exceptions = new List<Exception>();
        await foreach (var getForecastTask in Task.WhenEach(getForecastTasks).WithCancellation(cts.Token))
        {
            try
            {
                var forecastResult = await getForecastTask;

                // Cancel other weather provider api calls
                await cts.CancelAsync();

                return forecastResult;
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
