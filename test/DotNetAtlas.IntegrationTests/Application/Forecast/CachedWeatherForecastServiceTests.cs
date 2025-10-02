using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.Domain.Errors;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ZiggyCreatures.Caching.Fusion;

namespace DotNetAtlas.IntegrationTests.Application.Forecast;

[Collection<ForecastTestCollection>]
public class CachedWeatherForecastServiceTests : BaseIntegrationTest
{
    public CachedWeatherForecastServiceTests(IntegrationTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
    }

    [Fact]
    public async Task WhenSuccess_CachesByCacheKey_SecondCallHitsOnlyCache()
    {
        // Arrange
        var cache = Scope.ServiceProvider.GetRequiredService<IFusionCache>();
        var logger = Scope.ServiceProvider.GetRequiredService<ILogger<CachedWeatherForecastService>>();
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<ForecastCacheOptions>>();

        IReadOnlyList<ForecastDto> sample =
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
        decoratedMock.GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(sample));

        var sut = new CachedWeatherForecastService(decoratedMock, cache, logger, options);

        var request = new ForecastRequest("Prague", CountryCode.Cz, 1);

        // Act
        var firstResult = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);
        var secondResult = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            await decoratedMock.Received(1).GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>());
            firstResult.Should().BeSuccess();
            secondResult.Should().BeSuccess();
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
        decoratedMock.GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<IReadOnlyList<ForecastDto>>(
                WeatherForecastErrors.CityNotFoundError("UnknownCity", CountryCode.Cz)));

        var sut = new CachedWeatherForecastService(decoratedMock, cache, logger, options);

        var request = new ForecastRequest("UnknownCity", CountryCode.Cz, 2);

        // Act
        var first = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);
        var second = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeFailure();
            second.Should().BeFailure();
            await decoratedMock.Received(2).GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>());
        }
    }
}
