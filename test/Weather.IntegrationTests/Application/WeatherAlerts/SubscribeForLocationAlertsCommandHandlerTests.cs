using FluentResults.Extensions.FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Weather.Application.WeatherAlerts.SubscribeForLocationAlerts;
using Weather.Domain.Common.ValueObjects;
using Weather.IntegrationTests.Common;

namespace Weather.IntegrationTests.Application.WeatherAlerts;

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
