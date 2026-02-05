using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.Domain.Forecast.ValueObjects;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Infrastructure.HttpClients;

[Collection<ForecastTestCollection>]
public class WeatherApiComProviderIntegrationTests : BaseIntegrationTest
{
    private readonly WeatherApiComProvider _weatherApiComProvider;

    public WeatherApiComProviderIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
        _weatherApiComProvider = Scope.ServiceProvider.GetRequiredService<WeatherApiComProvider>();
    }

    [Fact]
    public async Task WhenAskedForForecastWithCorrectCity_ReturnsForecast()
    {
        // Arrange
        var forecastDateRange = DateRange.Create(DateOnly.FromDateTime(DateTime.UtcNow), 1).Value;
        var forecastCriteria = ForecastCriteria.Create("Prague", CountryCode.CZ, forecastDateRange).Value;

        // Act
        var forecastResult = await _weatherApiComProvider.GetForecastAsync(forecastCriteria, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().ContainSingle();
        }
    }
}
