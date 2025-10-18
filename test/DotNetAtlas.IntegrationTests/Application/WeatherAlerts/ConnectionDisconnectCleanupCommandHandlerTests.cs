using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class ConnectionDisconnectCleanupCommandHandlerTests : BaseIntegrationTest
{
    private readonly SubscribeForCityAlertsCommandHandler _subscribeForCityAlertsHandler;
    private readonly ConnectionDisconnectCleanupHandler _connectionDisconnectCleanupHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public ConnectionDisconnectCleanupCommandHandlerTests(IntegrationTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
        _subscribeForCityAlertsHandler = new SubscribeForCityAlertsCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IGroupManager>(),
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertJobScheduler>(),
            Scope.ServiceProvider.GetRequiredService<IGeocodingService>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<SubscribeForCityAlertsCommandHandler>>());

        _connectionDisconnectCleanupHandler = new ConnectionDisconnectCleanupHandler(
            Scope.ServiceProvider.GetRequiredService<IGroupManager>(),
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertJobScheduler>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<ConnectionDisconnectCleanupHandler>>());

        _jobStorageConnection =
            Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    [Fact]
    public async Task Cleanup_ShouldRemoveAllMembershipsForConnection()
    {
        // Arrange
        const string connectionId = "conn-clean-1";

        var subscribePrague = new SubscribeForCityAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = connectionId
        };
        var subscribeBerlin = new SubscribeForCityAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.DE,
            ConnectionId = connectionId
        };

        await _subscribeForCityAlertsHandler.HandleAsync(subscribePrague, TestContext.Current.CancellationToken);
        await _subscribeForCityAlertsHandler.HandleAsync(subscribeBerlin, TestContext.Current.CancellationToken);

        var groupManager = Scope.ServiceProvider.GetRequiredService<IGroupManager>();
        var groupInfoBeforeDisconnect = await groupManager.GetGroupsByConnectionIdAsync(connectionId);

        var recurringJobCountBeforeDisconnect = _jobStorageConnection.GetRecurringJobs().Count;

        // Act
        var connectionDisconnectCleanupResult = await _connectionDisconnectCleanupHandler.HandleAsync(
            new ConnectionDisconnectCleanupCommand(connectionId), TestContext.Current.CancellationToken);

        var groupInfoAfterDisconnect = await groupManager.GetGroupsByConnectionIdAsync(connectionId);
        var recurringJobCountAfterDisconnect = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            connectionDisconnectCleanupResult.Should().BeSuccess();

            groupInfoBeforeDisconnect.Count.Should().Be(2);
            groupInfoBeforeDisconnect.Should().AllSatisfy(groupInfo => groupInfo.MemberCount.Should().Be(1));
            recurringJobCountBeforeDisconnect.Should().Be(2);

            groupInfoAfterDisconnect.Should().BeEmpty();
            recurringJobCountAfterDisconnect.Should().Be(0);
        }
    }

    [Fact]
    public async Task Cleanup_ShouldNotRemoveOtherConnectionsMembership()
    {
        // Arrange
        const string connectionA = "conn-a";
        const string connectionB = "conn-b";
        const string city = "Vienna";
        const CountryCode country = CountryCode.AT;
        var groupManager = Scope.ServiceProvider.GetRequiredService<IGroupManager>();

        var expectedGroupName =
            WeatherAlertGroupNames.GroupByCitySubscriptionRequest(new AlertSubscriptionDto(city, country));

        var subscribeA = new SubscribeForCityAlertsCommand
        {
            City = city,
            CountryCode = country,
            ConnectionId = connectionA
        };
        var subscribeB = new SubscribeForCityAlertsCommand
        {
            City = city,
            CountryCode = country,
            ConnectionId = connectionB
        };

        await _subscribeForCityAlertsHandler.HandleAsync(subscribeA, TestContext.Current.CancellationToken);
        await _subscribeForCityAlertsHandler.HandleAsync(subscribeB, TestContext.Current.CancellationToken);

        var groupInfoForConnectionA = await groupManager.GetGroupsByConnectionIdAsync(connectionA);
        var groupInfoForConnectionB = await groupManager.GetGroupsByConnectionIdAsync(connectionB);

        // Act
        var connectionADisconnectCleanupResult = await _connectionDisconnectCleanupHandler.HandleAsync(
            new ConnectionDisconnectCleanupCommand(connectionA), TestContext.Current.CancellationToken);

        var groupInfoForAAfterDisconnectA = await groupManager.GetGroupsByConnectionIdAsync(connectionA);
        var groupInfoForBAfterDisconnectA = await groupManager.GetGroupsByConnectionIdAsync(connectionB);

        // Assert
        using (new AssertionScope())
        {
            connectionADisconnectCleanupResult.Should().BeSuccess();

            groupInfoForConnectionA.Count.Should().Be(1);
            groupInfoForConnectionB.Count.Should().Be(1);

            groupInfoForAAfterDisconnectA.Should().BeEmpty();
            groupInfoForBAfterDisconnectA.Should()
                .ContainSingle(g => g.GroupName == expectedGroupName && g.MemberCount == 1);
        }
    }
}
