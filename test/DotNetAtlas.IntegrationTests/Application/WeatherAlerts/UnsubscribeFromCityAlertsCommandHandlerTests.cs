using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class UnsubscribeFromCityAlertsCommandHandlerTests : BaseIntegrationTest
{
    private readonly SubscribeForCityAlertsCommandHandler _subscribeHandler;
    private readonly UnsubscribeFromCityAlertsCommandHandler _unsubscribeHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public UnsubscribeFromCityAlertsCommandHandlerTests(IntegrationTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
        _subscribeHandler = new SubscribeForCityAlertsCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IGroupManager>(),
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertJobScheduler>(),
            Scope.ServiceProvider.GetRequiredService<IGeocodingService>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<SubscribeForCityAlertsCommandHandler>>());

        _unsubscribeHandler = new UnsubscribeFromCityAlertsCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IGroupManager>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<UnsubscribeFromCityAlertsCommandHandler>>(),
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertJobScheduler>());

        _jobStorageConnection = Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    [Fact]
    public async Task WhenLastMember_UnschedulesJob()
    {
        // Arrange
        var subscribeCommand = new SubscribeForCityAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var unsubscribeCommand = new UnsubscribeFromCityAlertsCommand
        {
            City = subscribeCommand.City,
            CountryCode = subscribeCommand.CountryCode,
            ConnectionId = subscribeCommand.ConnectionId
        };

        var subscribeResult =
            await _subscribeHandler.HandleAsync(subscribeCommand, TestContext.Current.CancellationToken);
        subscribeResult.Should().BeSuccess();
        var recurringJobCountAfterSubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        var unsubscribeResult =
            await _unsubscribeHandler.HandleAsync(unsubscribeCommand, TestContext.Current.CancellationToken);
        var recurringJobCountAfterUnsubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            unsubscribeResult.Should().BeSuccess();
            recurringJobCountAfterSubscribe.Should().Be(1);
            recurringJobCountAfterUnsubscribe.Should().Be(0);
        }
    }

    [Fact]
    public async Task WhenPartOfGroup_RemovesFromTheGroup()
    {
        // Arrange
        var subscribeCommand = new SubscribeForCityAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var unsubscribeCommand = new UnsubscribeFromCityAlertsCommand
        {
            City = subscribeCommand.City,
            CountryCode = subscribeCommand.CountryCode,
            ConnectionId = subscribeCommand.ConnectionId
        };
        var groupManager = Scope.ServiceProvider.GetRequiredService<IGroupManager>();

        var subscribeResult =
            await _subscribeHandler.HandleAsync(subscribeCommand, TestContext.Current.CancellationToken);
        subscribeResult.Should().BeSuccess();
        var groupsAfterSubscribe = await groupManager.GetGroupsByConnectionIdAsync(subscribeCommand.ConnectionId);

        // Act
        var unsubscribeResult =
            await _unsubscribeHandler.HandleAsync(unsubscribeCommand, TestContext.Current.CancellationToken);
        var groupsAfterUnsubscribe = await groupManager.GetGroupsByConnectionIdAsync(subscribeCommand.ConnectionId);

        // Assert
        using (new AssertionScope())
        {
            unsubscribeResult.Should().BeSuccess();
            groupsAfterSubscribe.Should().ContainSingle();
            groupsAfterUnsubscribe.Should().BeEmpty();
        }
    }
}
