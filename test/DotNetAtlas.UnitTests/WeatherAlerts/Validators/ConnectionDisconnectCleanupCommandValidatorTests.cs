using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

public class ConnectionDisconnectCleanupCommandValidatorTests
{
    private readonly SendWeatherAlertCommandValidator _sendWeatherAlertCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var sendWeatherAlertCommand = new SendWeatherAlertCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            Message = new string('a', 50)
        };

        // Act
        var result = _sendWeatherAlertCommandValidator.TestValidate(sendWeatherAlertCommand);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyCity_ShouldFail()
    {
        // Arrange
        var sendWeatherAlertCommand = new SendWeatherAlertCommand
        {
            City = string.Empty,
            CountryCode = CountryCode.CZ,
            Message = "msg"
        };

        // Act
        var result = _sendWeatherAlertCommandValidator.TestValidate(sendWeatherAlertCommand);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.City);
    }

    [Fact]
    public void WhenTooLongMessage_ShouldFail()
    {
        // Arrange
        var sendWeatherAlertCommand = new SendWeatherAlertCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            Message = new string('x', 501)
        };

        // Act
        var result = _sendWeatherAlertCommandValidator.TestValidate(sendWeatherAlertCommand);

        // Assert
        result.ShouldHaveValidationErrorFor(c => c.Message);
    }
}
