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
