using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.FunctionalTests.Common.Clients;
using DotNetAtlas.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.FunctionalTests.SignalR;

[Collection<SignalRTestCollection>]
public class FakeWeatherAlertBackgroundJobTests : BaseApiTest
{
    public FakeWeatherAlertBackgroundJobTests(ApiTestFixture app)
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
        var jobInstance = Scope.ServiceProvider.GetRequiredService<FakeWeatherAlertBackgroundJob>();
        await jobInstance.SendWeatherAlert(alertSubscriptionDto, TestContext.Current.CancellationToken);

        var receivedAlertMessage =
            await plebSignalRClient.ConsumeOne(
                TimeSpan.FromMilliseconds(1000),
                TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            receivedAlertMessage.Should().NotBeNull();
            receivedAlertMessage.Message.Should().NotBeNullOrEmpty();
        }
    }
}
