using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Weather.Application.WeatherForecast.GetForecasts;
using Weather.Application.WeatherForecast.Services.Abstractions;
using Weather.Application.WeatherForecast.Services.Config;
using Weather.Domain.Forecast.ValueObjects;
using ZiggyCreatures.Caching.Fusion;

namespace Weather.Application.WeatherForecast.Services;

public class CachedWeatherForecastService : IWeatherForecastService
{
    private readonly IWeatherForecastService _decoratedForecastService;
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<CachedWeatherForecastService> _logger;
    private readonly ForecastCacheOptions _forecastCacheOptions;

    public CachedWeatherForecastService(
        IWeatherForecastService decoratedForecastService,
        IFusionCache fusionCache,
        ILogger<CachedWeatherForecastService> logger,
        IOptions<ForecastCacheOptions> options)
    {
        _decoratedForecastService = decoratedForecastService;
        _fusionCache = fusionCache;
        _logger = logger;
        _forecastCacheOptions = options.Value;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastCriteria forecastCriteria,
        CancellationToken ct)
    {
        Result<IReadOnlyList<ForecastDto>>? innerResult = null;

        try
        {
            var value = await _fusionCache.GetOrSetAsync<IReadOnlyList<ForecastDto>>(
                forecastCriteria.CacheKey(),
                factory: async (ctx, token) =>
                {
                    var result = await _decoratedForecastService.GetForecastAsync(forecastCriteria, token);
                    innerResult = result;
                    if (result.IsFailed)
                    {
                        ctx.Options.SetDurationZero();
                        return ctx.Fail("Failed result");
                    }

                    return result.Value;
                },
                cacheOptions =>
                {
                    cacheOptions
                        .SetDuration(TimeSpan.FromMinutes(_forecastCacheOptions.DurationMinutes))
                        .SetFailSafe(_forecastCacheOptions.EnableFailSafe,
                            TimeSpan.FromMinutes(_forecastCacheOptions.FailSafeMaxDurationMinutes),
                            TimeSpan.FromSeconds(_forecastCacheOptions.FailSafeThrottleSeconds))
                        .SetFactoryTimeouts(TimeSpan.FromMilliseconds(_forecastCacheOptions.FactorySoftTimeoutMs),
                            TimeSpan.FromMilliseconds(_forecastCacheOptions.FactoryHardTimeoutMs))
                        .SetEagerRefresh(_forecastCacheOptions.EagerRefreshThreshold);
                },
                tags: [forecastCriteria.CountryCode.ToString()],
                ct);

            return Result.Ok(value);
        }
        catch (FusionCacheFactoryException)
        {
            if (innerResult is { IsFailed: true })
            {
                return innerResult;
            }

            throw;
        }
    }
}
