using FluentValidation.TestHelper;
using Weather.Application.WeatherAlerts.RecordWeatherReading;

namespace Weather.UnitTests.WeatherAlerts.Validators;

public class RecordWeatherReadingCommandValidatorTests
{
    private readonly RecordWeatherReadingCommandValidator _recordWeatherReadingCommandValidator = new();

    [Fact]
    public void WhenValidCommand_ShouldPassValidation()
    {
        // Arrange
        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = Guid.CreateVersion7(),
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyMonitoredLocationId_ShouldFail()
    {
        // Arrange
        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = Guid.Empty,
            Readings =
            [
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 50.0,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor(c => c.MonitoredLocationId);
    }

    [Fact]
    public void WhenEmptyReadings_ShouldFail()
    {
        // Arrange
        var recordWeatherReadingCommand = new RecordWeatherReadingCommand
        {
            MonitoredLocationId = Guid.CreateVersion7(),
            Readings = []
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor(c => c.Readings);
    }
}
