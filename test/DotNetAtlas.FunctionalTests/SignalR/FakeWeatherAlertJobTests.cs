using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.FunctionalTests.SignalR;

[Collection<SignalRTestCollection>]
public class FakeWeatherAlertJobTests : BaseApiTest
{
    private readonly FakeWeatherAlertJob _fakeWeatherAlertJob;

    public FakeWeatherAlertJobTests(ApiTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
        _fakeWeatherAlertJob = new FakeWeatherAlertJob(
            Scope.ServiceProvider.GetRequiredService<ILogger<FakeWeatherAlertJob>>(),
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SendWeatherAlertCommand>>());
    }

    [Fact]
    public async Task WhenTriggered_ShouldSendAlertToGroup()
    {
        // Arrange
        await using var plebSignalRClient = await CreateSignalRClientAsync(ClientTypes.Pleb);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.Cz);
        await plebSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        // Act
        await _fakeWeatherAlertJob.SendWeatherAlert(alertSubscriptionDto, TestContext.Current.CancellationToken);

        var receivedAlertMessages =
            await plebSignalRClient.GetAllReceivedMessagesAsync();

        // Assert
        receivedAlertMessages.Should().ContainSingle();
    }
}
