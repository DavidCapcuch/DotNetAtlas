using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.Domain.Errors.Base;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteo;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Infrastructure.HttpProviders;

[Collection<ForecastTestCollection>]
public class OpenMeteoWeatherProviderIntegrationTests : BaseIntegrationTest
{
    private readonly OpenMeteoWeatherProvider _provider;

    public OpenMeteoWeatherProviderIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
        _provider = Scope.ServiceProvider.GetRequiredService<OpenMeteoWeatherProvider>();
    }

    [Fact]
    public async Task WhenAskedForForecastWithCorrectCity_ReturnsForecast()
    {
        // Arrange
        var forecastRequest = new ForecastRequest("Prague", CountryCode.CZ, 1);

        // Act
        var forecastResult = await _provider.GetForecastAsync(forecastRequest, TestContext.Current.CancellationToken);

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
        var forecastRequest = new ForecastRequest("asdfasdfsasdfsadsf", CountryCode.CZ, 1);

        // Act
        var forecastResult = await _provider.GetForecastAsync(forecastRequest, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeFailure();
            forecastResult.Errors.Should().ContainSingle();
            var error = forecastResult.Errors[0];
            error.Should().BeAssignableTo<NotFoundError>();
        }
    }
}
