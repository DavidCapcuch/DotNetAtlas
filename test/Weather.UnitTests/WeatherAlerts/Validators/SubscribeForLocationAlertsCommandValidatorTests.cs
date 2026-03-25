using FluentValidation.TestHelper;
using Weather.Application.WeatherAlerts.SubscribeForLocationAlerts;
using Weather.Domain.Common.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.Validators;

public class SubscribeForLocationAlertsCommandValidatorTests
{
    private readonly SubscribeForLocationAlertsCommandValidator _subscribeForLocationAlertsCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.DE,
            ConnectionId = "conn-1"
        };

        // Act
        var subscribeForLocationAlertsCommandValidationResult =
            _subscribeForLocationAlertsCommandValidator.TestValidate(subscribeForLocationAlertsCommand);

        // Assert
        subscribeForLocationAlertsCommandValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyConnectionId_ShouldFail()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.DE,
            ConnectionId = string.Empty
        };

        // Act
        var subscribeForLocationAlertsCommandValidationResult =
            _subscribeForLocationAlertsCommandValidator.TestValidate(subscribeForLocationAlertsCommand);

        // Assert
        subscribeForLocationAlertsCommandValidationResult.ShouldHaveValidationErrorFor(c => c.ConnectionId);
    }
}
