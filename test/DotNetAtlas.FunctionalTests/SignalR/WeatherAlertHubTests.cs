using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.FunctionalTests.Common.Clients;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using IGroupManager = DotNetAtlas.Application.WeatherAlerts.Common.Abstractions.IGroupManager;

namespace DotNetAtlas.FunctionalTests.SignalR;

[Collection<SignalRTestCollection>]
public class WeatherAlertHubTests : BaseApiTest
{
    public WeatherAlertHubTests(ApiTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task Subscribe_Unsubscribe_And_SendAlert_ShouldDeliverMessageToGroup()
    {
        // Arrange
        await using var plebSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Pleb);
        await using var devSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Dev);
        var alertSubscriptionDto = new AlertSubscriptionDto("Prague", CountryCode.CZ);
        var weatherAlerts = new[]
        {
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "Storm Warning")
        }.ToAsyncEnumerable();

        // Act
        await plebSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        await devSignalRClient.SendWeatherAlertAsync(weatherAlerts);

        var receivedAlertMessage =
            await plebSignalRClient.ConsumeOne(
                TimeSpan.FromMilliseconds(500),
                TestContext.Current.CancellationToken);

        await plebSignalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Assert
        using (new AssertionScope())
        {
            receivedAlertMessage.Should().NotBeNull();
            receivedAlertMessage.Message.Should().Be("Storm Warning");
        }
    }

    [Fact]
    public async Task MultipleAlerts_ShouldDeliverAllInOrder()
    {
        // Arrange
        await using var devSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Dev);
        var alertSubscriptionDto = new AlertSubscriptionDto("Berlin", CountryCode.DE);
        var weatherAlerts = new[]
        {
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A1"),
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A2"),
            new WeatherAlert(alertSubscriptionDto.City, alertSubscriptionDto.CountryCode, "A3")
        };
        var expectedAlertCount = weatherAlerts.Length;
        await devSignalRClient.SubscribeForCityAlertsAsync(alertSubscriptionDto);

        // Act
        await devSignalRClient.SendWeatherAlertAsync(weatherAlerts.ToAsyncEnumerable());

        var receivedAlertMessages =
            await devSignalRClient.ConsumeMultiple(
                TimeSpan.FromMilliseconds(500),
                maxCount: expectedAlertCount,
                TestContext.Current.CancellationToken);

        await devSignalRClient.UnsubscribeFromCityAlertsAsync(alertSubscriptionDto);

        // Assert
        using (new AssertionScope())
        {
            receivedAlertMessages.Should().HaveCount(expectedAlertCount);
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
        await using var plebSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Pleb);
        await using var devSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Dev);

        var subscription = new AlertSubscriptionDto("Madrid", CountryCode.ES);
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
        var nonAuthSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.NonAuth);

        var alertSubscriptionDto = new AlertSubscriptionDto("Vienna", CountryCode.AT);
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
        await using var plebSignalRClient = await SignalRClientFactory.CreateAsync(ClientType.Pleb);
        var weatherAlerts = new[]
        {
            new WeatherAlert("Rome", CountryCode.IT, "Denied"),
        }.ToAsyncEnumerable();

        // Act + Assert
        await plebSignalRClient
            .Invoking(async c =>
                await c.Connection.InvokeAsync(nameof(IWeatherAlertHubContract.SendWeatherAlert), weatherAlerts))
            .Should()
            .ThrowAsync<HubException>().WithMessage("Failed to invoke 'SendWeatherAlert' because user is unauthorized");
    }
}
