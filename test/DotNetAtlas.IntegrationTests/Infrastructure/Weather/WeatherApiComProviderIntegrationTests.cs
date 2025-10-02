using DotNetAtlas.Application.Forecast.Services.Requests;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiComProvider;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Infrastructure.Weather;

[Collection<ForecastTestCollection>]
public class WeatherApiComProviderIntegrationTests : BaseIntegrationTest
{
    private readonly WeatherApiComProvider _provider;

    public WeatherApiComProviderIntegrationTests(IntegrationTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
        _provider = Scope.ServiceProvider.GetRequiredService<WeatherApiComProvider>();
    }

    [Fact]
    public async Task WhenAskedForForecastWithCorrectCity_ReturnsForecast()
    {
        // Arrange
        var forecastRequest = new ForecastRequest("Prague", CountryCode.Cz, 1);

        // Act
        var forecastResult = await _provider.GetForecastAsync(forecastRequest, TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            forecastResult.Should().BeSuccess();
            forecastResult.Value.Should().ContainSingle();
        }
    }
}
