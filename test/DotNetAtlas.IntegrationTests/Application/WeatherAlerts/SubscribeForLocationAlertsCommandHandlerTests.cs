using DotNetAtlas.Application.WeatherAlerts.SubscribeForLocationAlerts;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class SubscribeForLocationAlertsCommandHandlerTests : BaseIntegrationTest
{
    private readonly ICommandHandler<SubscribeForLocationAlertsCommand> _subscribeForLocationAlertsCommandHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public SubscribeForLocationAlertsCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _subscribeForLocationAlertsCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SubscribeForLocationAlertsCommand>>();

        _jobStorageConnection =
            Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    /// <summary>
    /// DEMO: Jobs are scheduled on every subscribe call. Hangfire's AddOrUpdate is idempotent,
    /// so duplicate calls just update the existing job.
    /// </summary>
    [Fact]
    public async Task WhenSubscribing_SchedulesAlertJob()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var expectedGroupName = AlertGroup.From(
            City.Create(subscribeForLocationAlertsCommand.City).Value,
            subscribeForLocationAlertsCommand.CountryCode).GroupName;

        // Act
        var subscribeForLocationAlertsResult = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
            subscribeForLocationAlertsCommand,
            TestContext.Current.CancellationToken);

        var recurringJobCountAfterSubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            subscribeForLocationAlertsResult.Should().BeSuccess();
            recurringJobCountAfterSubscribe.Should().Be(1);
        }
    }

    [Fact]
    public async Task WhenUnknownCity_ReturnsFailure()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = new string('X', 10),
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-2"
        };

        // Act
        var subscribeForLocationAlertsResult = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
            subscribeForLocationAlertsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        subscribeForLocationAlertsResult.Should().BeFailure();
    }
}
