using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

public class SubscribeForCityAlertsCommandValidatorTests
{
    private readonly SubscribeForCityAlertsCommandValidator _subscribeForCityAlertsCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var subscribeForCityAlertsCommand = new SubscribeForCityAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.De,
            ConnectionId = "conn-1"
        };

        // Act
        var result = _subscribeForCityAlertsCommandValidator.TestValidate(subscribeForCityAlertsCommand);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyConnectionId_ShouldFail()
    {
        // Arrange
        var subscribeForCityAlertsCommand = new SubscribeForCityAlertsCommand
        {
            City = "Berlin",
            CountryCode = CountryCode.De,
            ConnectionId = string.Empty
        };

        // Act
        var result = _subscribeForCityAlertsCommandValidator.TestValidate(subscribeForCityAlertsCommand);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.ConnectionId);
    }
}
