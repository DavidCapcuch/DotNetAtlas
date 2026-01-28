using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromLocationAlerts;
using DotNetAtlas.Domain.Common.ValueObjects;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

public class UnsubscribeFromLocationAlertsCommandValidatorTests
{
    private readonly UnsubscribeFromLocationAlertsCommandValidator _unsubscribeFromLocationAlertsCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var unsubscribeFromLocationAlertsCommand = new UnsubscribeFromLocationAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.DE,
            ConnectionId = "conn-1"
        };

        // Act
        var unsubscribeFromLocationAlertsCommandValidationResult =
            _unsubscribeFromLocationAlertsCommandValidator.TestValidate(unsubscribeFromLocationAlertsCommand);

        // Assert
        unsubscribeFromLocationAlertsCommandValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyCity_ShouldFail()
    {
        // Arrange
        var unsubscribeFromLocationAlertsCommand = new UnsubscribeFromLocationAlertsCommand
        {
            City = "",
            CountryCode = CountryCode.DE,
            ConnectionId = "conn-1"
        };

        // Act
        var unsubscribeFromLocationAlertsCommandValidationResult =
            _unsubscribeFromLocationAlertsCommandValidator.TestValidate(unsubscribeFromLocationAlertsCommand);

        // Assert
        unsubscribeFromLocationAlertsCommandValidationResult.ShouldHaveValidationErrorFor(c => c.City);
    }
}
