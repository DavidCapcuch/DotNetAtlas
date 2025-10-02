using DotNetAtlas.Application.WeatherAlerts.Common.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using DotNetAtlas.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.IntegrationTests.Application.WeatherAlerts;

[Collection<SignalRTestCollection>]
public class SendWeatherAlertCommandHandlerTests : BaseIntegrationTest
{
    private readonly SendWeatherAlertCommandHandler _sendWeatherAlertCommandHandler;

    public SendWeatherAlertCommandHandlerTests(IntegrationTestFixture app, ITestOutputHelper output)
        : base(app, output)
    {
        _sendWeatherAlertCommandHandler = new SendWeatherAlertCommandHandler(
            Scope.ServiceProvider.GetRequiredService<IWeatherAlertNotifier>(),
            Scope.ServiceProvider.GetRequiredService<ILogger<SendWeatherAlertCommandHandler>>());
    }

    [Fact]
    public async Task WhenCalledWithValidData_SendsSuccessfully()
    {
        // Arrange
        var sendWeatherAlertCommand = new SendWeatherAlertCommand
        {
            City = "Prague",
            CountryCode = CountryCode.Cz,
            Message = "Heads up!"
        };

        // Act
        var sendWeatherAlertResult =
            await _sendWeatherAlertCommandHandler.HandleAsync(
                sendWeatherAlertCommand,
                TestContext.Current.CancellationToken);

        // Assert
        sendWeatherAlertResult.Should().BeSuccess();
    }
}
