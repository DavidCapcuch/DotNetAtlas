using DotNetAtlas.Application.WeatherAlerts.SubscribeForLocationAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class UnsubscribeFromLocationAlertsCommandHandlerTests : BaseIntegrationTest
{
    private readonly ICommandHandler<SubscribeForLocationAlertsCommand> _subscribeForLocationAlertsCommandHandler;
    private readonly ICommandHandler<UnsubscribeFromLocationAlertsCommand> _unsubscribeFromLocationAlertsCommandHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public UnsubscribeFromLocationAlertsCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _subscribeForLocationAlertsCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SubscribeForLocationAlertsCommand>>();

        _unsubscribeFromLocationAlertsCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<UnsubscribeFromLocationAlertsCommand>>();

        _jobStorageConnection =
            Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    /// <summary>
    /// DEMO: Unsubscribe does NOT unschedule jobs - jobs are cleaned up only on application startup.
    /// This test verifies that the job remains scheduled after unsubscribe (simplified demo behavior).
    /// </summary>
    [Fact]
    public async Task WhenUnsubscribing_JobRemainsScheduled()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var unsubscribeFromLocationAlertsCommand = new UnsubscribeFromLocationAlertsCommand
        {
            City = subscribeForLocationAlertsCommand.City,
            CountryCode = subscribeForLocationAlertsCommand.CountryCode,
            ConnectionId = subscribeForLocationAlertsCommand.ConnectionId
        };

        // Act
        var subscribeForLocationAlertsResult = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
            subscribeForLocationAlertsCommand,
            TestContext.Current.CancellationToken);
        subscribeForLocationAlertsResult.Should().BeSuccess();
        var recurringJobCountAfterSubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        var unsubscribeFromLocationAlertsResult = await _unsubscribeFromLocationAlertsCommandHandler.HandleAsync(
            unsubscribeFromLocationAlertsCommand,
            TestContext.Current.CancellationToken);
        var recurringJobCountAfterUnsubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            unsubscribeFromLocationAlertsResult.Should().BeSuccess();
            recurringJobCountAfterSubscribe.Should().Be(1);
            // DEMO: Job is NOT unscheduled on unsubscribe - cleanup happens only on startup
            recurringJobCountAfterUnsubscribe.Should().Be(1);
        }
    }

    [Fact]
    public async Task WhenPartOfGroup_RemovesFromTheGroup()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };
        var unsubscribeFromLocationAlertsCommand = new UnsubscribeFromLocationAlertsCommand
        {
            City = subscribeForLocationAlertsCommand.City,
            CountryCode = subscribeForLocationAlertsCommand.CountryCode,
            ConnectionId = subscribeForLocationAlertsCommand.ConnectionId
        };

        var subscribeForLocationAlertsResult = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
            subscribeForLocationAlertsCommand,
            TestContext.Current.CancellationToken);
        subscribeForLocationAlertsResult.Should().BeSuccess();

        // Act
        var unsubscribeFromLocationAlertsResult = await _unsubscribeFromLocationAlertsCommandHandler.HandleAsync(
            unsubscribeFromLocationAlertsCommand,
            TestContext.Current.CancellationToken);

        // Assert
        unsubscribeFromLocationAlertsResult.Should().BeSuccess();
    }
}
