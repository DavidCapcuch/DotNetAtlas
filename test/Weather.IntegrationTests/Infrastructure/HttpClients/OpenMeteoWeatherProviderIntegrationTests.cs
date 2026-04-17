using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Platform.SharedKernel.Errors;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.ValueObjects;
using Weather.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Infrastructure.HttpClients;

[Collection<IntegrationTestCollection>]
public class OpenMeteoWeatherProviderIntegrationTests : BaseIntegrationTest
{
    private readonly OpenMeteoWeatherProvider _openMeteoWeatherProvider;

    public OpenMeteoWeatherProviderIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
        _openMeteoWeatherProvider = Scope.ServiceProvider.GetRequiredService<OpenMeteoWeatherProvider>();
    }

    [Fact]
    public async Task WhenAskedForForecastWithCorrectCity_ReturnsForecast()
    {
        // Arrange
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 1).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var forecastResult = await _openMeteoWeatherProvider.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task WhenAskedForForecastWithNonExistentCity_ReturnsCityNotFoundError()
    {
        // Arrange
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 2).Value;
        var forecastCriteria = ForecastCriteria.Create("asdfasdfsasdfsadsf", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var forecastResult = await _openMeteoWeatherProvider.GetForecastAsync(
            forecastCriteria,
            TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeFailure();
            forecastResult.Errors.Should().ContainSingle();
            var forecastError = forecastResult.Errors[0];
            forecastError.Should().BeAssignableTo<NotFoundError>();
        }
    }
}
