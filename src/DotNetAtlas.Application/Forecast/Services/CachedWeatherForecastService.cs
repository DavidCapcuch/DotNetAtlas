using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using FluentResults;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace DotNetAtlas.Application.Forecast.Services;

public class CachedWeatherForecastService : IWeatherForecastService
{
    private readonly IWeatherForecastService _decoratedForecastService;
    private readonly IFusionCache _fusionCache;
    private readonly ILogger<CachedWeatherForecastService> _logger;
    private readonly ForecastCacheOptions _options;

    public CachedWeatherForecastService(
        IWeatherForecastService decoratedForecastService,
        IFusionCache fusionCache,
        ILogger<CachedWeatherForecastService> logger,
        Microsoft.Extensions.Options.IOptions<ForecastCacheOptions> options)
    {
        _decoratedForecastService = decoratedForecastService;
        _fusionCache = fusionCache;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<ForecastDto>>> GetForecastAsync(
        ForecastRequest forecastRequest,
        CancellationToken ct)
    {
        Result<IReadOnlyList<ForecastDto>>? innerResult = null;

        try
        {
            var value = await _fusionCache.GetOrSetAsync<IReadOnlyList<ForecastDto>>(
                forecastRequest.CacheKey,
                factory: async (ctx, token) =>
                {
                    var result = await _decoratedForecastService.GetForecastAsync(forecastRequest, token);
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
                        .SetDuration(TimeSpan.FromMinutes(_options.DurationMinutes))
                        .SetFailSafe(_options.EnableFailSafe,
                            TimeSpan.FromMinutes(_options.FailSafeMaxDurationMinutes),
                            TimeSpan.FromSeconds(_options.FailSafeThrottleSeconds))
                        .SetFactoryTimeouts(TimeSpan.FromMilliseconds(_options.FactorySoftTimeoutMs),
                            TimeSpan.FromMilliseconds(_options.FactoryHardTimeoutMs))
                        .SetEagerRefresh(_options.EagerRefreshThreshold);
                },
                tags: [forecastRequest.CountryCode.ToString()],
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
