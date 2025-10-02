using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.Services;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotNetAtlas.IntegrationTests.Application.Forecast;

[Collection<ForecastTestCollection>]
public class HedgingWeatherForecastServiceTests : BaseIntegrationTest
{
    private readonly ILogger<HedgingWeatherForecastService> _logger;

    public HedgingWeatherForecastServiceTests(IntegrationTestFixture app, ITestOutputHelper testOutputHelper)
        : base(app, testOutputHelper)
    {
        _logger = Scope.ServiceProvider.GetRequiredService<ILogger<HedgingWeatherForecastService>>();
    }

    [Fact]
    public async Task WhenFunctioningPrimaryProvider_ReturnsForecast()
    {
        // Arrange
        var realPrimary = Scope.ServiceProvider.GetRequiredService<IMainWeatherForecastProvider>();
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();

        var badProvider1 = Substitute.For<IWeatherForecastProvider>();
        badProvider1
            .GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var badProvider2 = Substitute.For<IWeatherForecastProvider>();
        badProvider2
            .GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        IEnumerable<IWeatherForecastProvider> badProviders = [badProvider1, badProvider2];

        var sut = new HedgingWeatherForecastService(realPrimary, badProviders, _logger, options);
        var request = new ForecastRequest("Prague", CountryCode.Cz, 3);

        // Act
        var forecastResult = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task WhenPrimaryThrows_HedgeSucceeds_ReturnsForecasts()
    {
        // Arrange
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();
        var realProviders = Scope.ServiceProvider.GetServices<IWeatherForecastProvider>().ToList();

        var badMainProvider = Substitute.For<IMainWeatherForecastProvider>();
        badMainProvider
            .GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        // Use one real and one throwing mock
        var mixedProviders = new List<IWeatherForecastProvider>
        {
            realProviders.First(),
            badMainProvider
        };
        var sut = new HedgingWeatherForecastService(badMainProvider, mixedProviders, _logger, options);
        var request = new ForecastRequest("Prague", CountryCode.Cz, 2);

        // Act
        var forecastResult = await sut.GetForecastAsync(request, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task WhenAllProvidersFail_ThrowsAggregateException()
    {
        // Arrange
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();

        var primary = Substitute.For<IMainWeatherForecastProvider>();
        primary
            .GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var secondary = Substitute.For<IWeatherForecastProvider>();
        secondary
            .GetForecastAsync(Arg.Any<ForecastRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var sut = new HedgingWeatherForecastService(primary, [primary, secondary], _logger, options);
        var request = new ForecastRequest("Prague", CountryCode.Cz, 1);

        // Act
        var act = () => sut.GetForecastAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowExactlyAsync<AggregateException>();
    }
}
