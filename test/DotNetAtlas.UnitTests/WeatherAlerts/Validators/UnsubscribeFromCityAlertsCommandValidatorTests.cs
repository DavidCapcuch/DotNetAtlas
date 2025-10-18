using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

public class UnsubscribeFromCityAlertsCommandValidatorTests
{
    private readonly UnsubscribeFromCityAlertsCommandValidator _unsubscribeFromCityAlertsCommandValidator = new();

    [Fact]
    public void GivenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var unsubscribeFromCityAlertsCommand = new UnsubscribeFromCityAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.DE,
            ConnectionId = "conn-1"
        };

        // Act
        var result = _unsubscribeFromCityAlertsCommandValidator.TestValidate(unsubscribeFromCityAlertsCommand);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GivenEmptyCity_ShouldFail()
    {
        // Arrange
        var cmd = new UnsubscribeFromCityAlertsCommand
        {
            City = "",
            CountryCode = CountryCode.DE,
            ConnectionId = "conn-1"
        };

        // Act
        var result = _unsubscribeFromCityAlertsCommandValidator.TestValidate(cmd);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.City);
    }
}
