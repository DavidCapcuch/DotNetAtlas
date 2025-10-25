using DotNetAtlas.Application.WeatherAlerts.Common;
using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.Common.Contracts;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class SubscribeForCityAlertsCommandHandlerTests : BaseIntegrationTest
{
    private readonly SubscribeForCityAlertsCommandHandler _subscribeHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public SubscribeForCityAlertsCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _subscribeHandler = new SubscribeForCityAlertsCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IGroupManager>(),
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertJobScheduler>(),
            Scope.ServiceProvider.GetRequiredService<IGeocodingService>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<SubscribeForCityAlertsCommandHandler>>());

        _jobStorageConnection =
            Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    [Fact]
    public async Task WhenFirstMember_SchedulesAlertJob()
    {
        // Arrange
        var subscribeCommand = new SubscribeForCityAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var expectedGroupName =
            WeatherAlertGroupNames.GroupByCitySubscriptionRequest(new AlertSubscriptionDto(subscribeCommand.City,
                subscribeCommand.CountryCode));

        // Act
        var subscribeResult =
            await _subscribeHandler.HandleAsync(subscribeCommand, TestContext.Current.CancellationToken);

        var groups = await Scope.ServiceProvider.GetRequiredService<IGroupManager>()
            .GetGroupsByConnectionIdAsync(subscribeCommand.ConnectionId);
        var recurringJobCountAfterSubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            subscribeResult.Should().BeSuccess();
            groups.Should().ContainSingle(g => g.GroupName == expectedGroupName && g.MemberCount == 1);
            recurringJobCountAfterSubscribe.Should().Be(1);
        }
    }

    [Fact]
    public async Task WhenUnknownCity_ReturnsFailure()
    {
        // Arrange
        var cmd = new SubscribeForCityAlertsCommand
        {
            City = new string('X', 10),
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-2"
        };

        // Act
        var result = await _subscribeHandler.HandleAsync(cmd, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
    }
}
