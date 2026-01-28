using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherForecast.Services.Config;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.Domain.Forecast.Errors;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace DotNetAtlas.IntegrationTests.Application.WeatherForecast;

[Collection<ForecastTestCollection>]
public class CachedWeatherForecastServiceTests : BaseIntegrationTest
{
    public CachedWeatherForecastServiceTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenSuccess_CachesByCacheKey_SecondCallHitsOnlyCache()
    {
        // Arrange
        var cache = Scope.ServiceProvider.GetRequiredService<IFusionCache>();
        var logger = Scope.ServiceProvider.GetRequiredService<ILogger<CachedWeatherForecastService>>();
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<ForecastCacheOptions>>();

        IReadOnlyList<ForecastDto> sampleForecasts =
        [
            new ForecastDto
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                MaxTemperatureC = 12,
                MinTemperatureC = 3,
                Summary = "Sunny"
            }
        ];

        var decoratedMock = Substitute.For<IWeatherForecastService>();
        decoratedMock.GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(sampleForecasts));

        var cachedWeatherForecastService = new CachedWeatherForecastService(decoratedMock, cache, logger, options);

        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 12).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var firstForecastResult = await cachedWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);
        var secondForecastResult = await cachedWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            await decoratedMock.Received(1).GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>());
            firstForecastResult.Should().BeSuccess();
            secondForecastResult.Should().BeSuccess();
        }
    }

    [Fact]
    public async Task WhenFailure_IsNotCached_SubsequentCallInvokesAgain()
    {
        // Arrange
        var cache = Scope.ServiceProvider.GetRequiredService<IFusionCache>();
        var logger = Scope.ServiceProvider.GetRequiredService<ILogger<CachedWeatherForecastService>>();
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<ForecastCacheOptions>>();

        var decoratedMock = Substitute.For<IWeatherForecastService>();
        decoratedMock.GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<IReadOnlyList<ForecastDto>>(
                ForecastErrors.CityNotFoundError("UnknownCity", CountryCode.CZ)));

        var cachedWeatherForecastService = new CachedWeatherForecastService(decoratedMock, cache, logger, options);

        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 3).Value;
        var forecastCriteria = ForecastCriteria.Create("UnknownCity", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var firstForecastResult = await cachedWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);
        var secondForecastResult = await cachedWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            firstForecastResult.Should().BeFailure();
            secondForecastResult.Should().BeFailure();
            await decoratedMock.Received(2).GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>());
        }
    }
}
