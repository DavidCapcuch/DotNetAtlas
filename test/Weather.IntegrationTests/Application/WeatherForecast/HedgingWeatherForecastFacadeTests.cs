using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Weather.Application.WeatherForecast.Services;
using Weather.Application.WeatherForecast.Services.Abstractions;
using Weather.Application.WeatherForecast.Services.Config;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.ValueObjects;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Application.WeatherForecast;

[Collection<IntegrationTestCollection>]
public class HedgingWeatherForecastServiceTests : BaseIntegrationTest
{
    private readonly ILogger<HedgingWeatherForecastService> _logger;

    public HedgingWeatherForecastServiceTests(IntegrationTestFixture app)
        : base(app)
    {
        _logger = Scope.ServiceProvider.GetRequiredService<ILogger<HedgingWeatherForecastService>>();
    }

    [Fact]
    public async Task WhenFunctioningPrimaryProvider_ReturnsForecast()
    {
        // Arrange
        const int numberOfDaysForecast = 4;

        var realPrimary = Scope.ServiceProvider.GetRequiredService<IMainWeatherForecastProvider>();
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();

        var badProvider1 = Substitute.For<IWeatherForecastProvider>();
        badProvider1
            .GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var badProvider2 = Substitute.For<IWeatherForecastProvider>();
        badProvider2
            .GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        IEnumerable<IWeatherForecastProvider> badProviders = [badProvider1, badProvider2];

        var hedgingWeatherForecastService =
            new HedgingWeatherForecastService(realPrimary, badProviders, _logger, options);
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), numberOfDaysForecast).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var forecastResult = await hedgingWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().HaveCount(numberOfDaysForecast);
        }
    }

    [Fact]
    public async Task WhenPrimaryThrows_HedgeSucceeds_ReturnsForecasts()
    {
        // Arrange
        const int numberOfDaysForecast = 3;
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();
        var realProviders = Scope.ServiceProvider.GetServices<IWeatherForecastProvider>().ToList();

        var badMainProvider = Substitute.For<IMainWeatherForecastProvider>();
        badMainProvider
            .GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        // Use one real and one throwing mock
        var mixedProviders = new List<IWeatherForecastProvider>
        {
            realProviders.First(),
            badMainProvider
        };
        var hedgingWeatherForecastService =
            new HedgingWeatherForecastService(badMainProvider, mixedProviders, _logger, options);
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), numberOfDaysForecast).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var forecastResult = await hedgingWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().HaveCount(numberOfDaysForecast);
        }
    }

    [Fact]
    public async Task WhenAllProvidersFail_ThrowsAggregateException()
    {
        // Arrange
        var options = Scope.ServiceProvider.GetRequiredService<IOptions<WeatherHedgingOptions>>();

        var primary = Substitute.For<IMainWeatherForecastProvider>();
        primary
            .GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var secondary = Substitute.For<IWeatherForecastProvider>();
        secondary
            .GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<InvalidOperationException>();

        var hedgingWeatherForecastService =
            new HedgingWeatherForecastService(primary, [primary, secondary], _logger, options);
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 2).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var getForecastAction = () => hedgingWeatherForecastService.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        await getForecastAction.Should().ThrowExactlyAsync<AggregateException>();
    }
}
