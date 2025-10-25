using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.FunctionalTests.Common.Clients;
using DotNetAtlas.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.FunctionalTests.SignalR;

[Collection<SignalRTestCollection>]
public class FakeWeatherAlertJobTests : BaseApiTest
{
    public FakeWeatherAlertJobTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task WhenJobExecutes_ShouldSendWeatherAlertToSubscribedClients()
    {
        // Arrange
        await using var plebSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Pleb);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.CZ);
        await plebSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        // Act
        var jobInstance = Scope.ServiceProvider.GetRequiredService<FakeWeatherAlertJob>();
        await jobInstance.SendWeatherAlert(alertSubscriptionDto, TestContext.Current.CancellationToken);

        await Task.Delay(1000, TestContext.Current.CancellationToken);

        var receivedAlertMessages =
            await plebSignalRClient.GetAllReceivedMessagesAsync(
                TimeSpan.FromMilliseconds(500),
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            receivedAlertMessages.Should().NotBeEmpty("Job should send at least one alert");
            receivedAlertMessages.Should().AllSatisfy(msg =>
                msg.Message.Should().NotBeNullOrEmpty("Each alert should have a message"));
        }
    }
}
