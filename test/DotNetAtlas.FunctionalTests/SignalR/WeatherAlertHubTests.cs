using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using IGroupManager = DotNetAtlas.Application.WeatherAlerts.Common.Abstractions.IGroupManager;

namespace DotNetAtlas.FunctionalTests.SignalR;

[Collection<SignalRTestCollection>]
public class WeatherAlertHubTests : BaseApiTest
{
    public WeatherAlertHubTests(ApiTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
    }

    [Fact]
    public async Task Subscribe_Unsubscribe_And_SendAlert_ShouldDeliverMessageToGroup()
    {
        // Arrange
        await using var plebSignalRClient = await CreateSignalRClientAsync(ClientTypes.Pleb);
        await using var devSignalRClient = await CreateSignalRClientAsync(ClientTypes.Dev);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.Cz);
        var weatherAlerts = new[]
        {
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "Storm Warning")
        }.ToAsyncEnumerable();

        // Act
        await plebSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        await devSignalRClient.SendWeatherAlertAsync(weatherAlerts);

        var receivedAlertMessages =
            await plebSignalRClient.GetAllReceivedMessagesAsync();

        await plebSignalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Assert
        receivedAlertMessages.Should().ContainSingle(m => m.Message == "Storm Warning");
    }

    [Fact]
    public async Task MultipleAlerts_ShouldDeliverAllInOrder()
    {
        // Arrange
        await using var devSignalRClient = await CreateSignalRClientAsync(ClientTypes.Dev);
        var alertSubscriptionDto = new AlertSubscriptionDto("Berlin", CountryCode.De);
        var weatherAlerts = new[]
        {
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A1"),
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A2"),
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A3")
        }.ToAsyncEnumerable();
        await devSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        // Act
        await devSignalRClient.SendWeatherAlertAsync(weatherAlerts);

        var receivedAlertMessages = await devSignalRClient.GetAllReceivedMessagesAsync();

        await devSignalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Assert
        using (new AssertionScope())
        {
            receivedAlertMessages.Should().HaveCount(3);
            receivedAlertMessages.Should().ContainInOrder(
                new WeatherAlertMessage("A1"),
                new WeatherAlertMessage("A2"),
                new WeatherAlertMessage("A3"));
        }
    }

    [Fact]
    public async Task Unsubscribe_ShouldStopFurtherDeliveries()
    {
        // Arrange
        await using var plebSignalRClient = await CreateSignalRClientAsync(ClientTypes.Pleb);
        await using var devSignalRClient = await CreateSignalRClientAsync(ClientTypes.Dev);

        var subscription = new AlertSubscriptionDto("Madrid", CountryCode.Es);
        var weatherAlerts = new[]
        {
            new WeatherAlert(subscription.City, subscription.CountryCode, "ShouldNotArrive")
        }.ToAsyncEnumerable();

        await plebSignalRClient.SubscribeForCityAlertsAsync(subscription);
        await plebSignalRClient.UnsubscribeFromCityAlertsAsync(subscription);

        // Act
        await devSignalRClient.SendWeatherAlertAsync(weatherAlerts);

        // Assert
        plebSignalRClient.ReceivedMessages.Reader.TryRead(out _).Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_ShouldRemoveMembershipFromGroupManager()
    {
        // Arrange
        await using var nonAuthSignalRClient = await CreateSignalRClientAsync(ClientTypes.NonAuth);

        var alertSubscriptionDto = new AlertSubscriptionDto("Vienna", CountryCode.At);
        await nonAuthSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var connectionId = nonAuthSignalRClient.Connection.ConnectionId!;

        var groupManager = Scope.ServiceProvider
            .GetRequiredService<IGroupManager>();
        var groupsBeforeDisconnect = await groupManager.GetGroupsByConnectionIdAsync(connectionId);

        // Act
        await nonAuthSignalRClient.DisposeAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var groupsAfterDisconnect = await groupManager.GetGroupsByConnectionIdAsync(connectionId);

        // Assert
        using (new AssertionScope())
        {
            groupsBeforeDisconnect.Should().ContainSingle(g =>
                g.GroupName.Contains(alertSubscriptionDto.City, StringComparison.OrdinalIgnoreCase));
            groupsAfterDisconnect.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NormalUser_ShouldNotBeAbleToSendAlerts()
    {
        // Arrange
        await using var plebSignalRClient = await CreateSignalRClientAsync(ClientTypes.Pleb);
        var weatherAlerts = new[]
        {
            new WeatherAlert("Rome", CountryCode.It, "Denied"),
        }.ToAsyncEnumerable();

        // Act + Assert
        await plebSignalRClient
            .Invoking(async c =>
                await c.Connection.InvokeAsync(nameof(IWeatherAlertHubContract.SendWeatherAlert), weatherAlerts))
            .Should()
            .ThrowAsync<HubException>().WithMessage("Failed to invoke 'SendWeatherAlert' because user is unauthorized");
    }
}
