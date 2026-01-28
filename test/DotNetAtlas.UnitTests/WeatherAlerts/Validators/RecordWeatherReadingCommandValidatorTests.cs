using DotNetAtlas.Application.WeatherAlerts.RecordWeatherReading;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Validators;

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

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void WhenHumidityOutOfRange_ShouldFail(double invalidHumidity)
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
                    HumidityPercent = invalidHumidity,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor("Readings[0].HumidityPercent");
    }

    [Fact]
    public void WhenNegativeWindSpeed_ShouldFail()
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
                    WindSpeedKmh = -1.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor("Readings[0].WindSpeedKmh");
    }

    [Fact]
    public void WhenDefaultRecordedAtUtc_ShouldFail()
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
                    RecordedAtUtc = default
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor("Readings[0].RecordedAtUtc");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void WhenHumidityAtBoundary_ShouldPassValidation(double boundaryHumidity)
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
                    HumidityPercent = boundaryHumidity,
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldNotHaveValidationErrorFor("Readings[0].HumidityPercent");
    }

    [Fact]
    public void WhenZeroWindSpeed_ShouldPassValidation()
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
                    WindSpeedKmh = 0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldNotHaveValidationErrorFor("Readings[0].WindSpeedKmh");
    }

    [Fact]
    public void WhenMultipleReadingsWithOneInvalid_ShouldFailForInvalidOnly()
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
                },
                new WeatherReadingDto
                {
                    TemperatureC = 25.0,
                    HumidityPercent = 150.0, // Invalid
                    WindSpeedKmh = 15.0,
                    RecordedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        };

        // Act
        var recordWeatherReadingCommandValidationResult =
            _recordWeatherReadingCommandValidator.TestValidate(recordWeatherReadingCommand);

        // Assert
        recordWeatherReadingCommandValidationResult.ShouldNotHaveValidationErrorFor("Readings[0].HumidityPercent");
        recordWeatherReadingCommandValidationResult.ShouldHaveValidationErrorFor("Readings[1].HumidityPercent");
    }
}
