using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.ValueObjects;
using Weather.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Infrastructure.HttpClients;

[Collection<IntegrationTestCollection>]
public class WeatherApiComProviderIntegrationTests : BaseIntegrationTest
{
    private readonly WeatherApiComProvider _weatherApiComProvider;

    public WeatherApiComProviderIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
        _weatherApiComProvider = Scope.ServiceProvider.GetRequiredService<WeatherApiComProvider>();
    }
}
