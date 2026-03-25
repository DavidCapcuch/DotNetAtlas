using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Weather.Domain.Alerts.ValueObjects;

namespace Weather.UnitTests.WeatherAlerts.ValueObjects;

public class WeatherAlertTests
{
    [Fact]
    public void Create_WhenValidInput_ReturnsSuccess()
    {
        // Arrange
        const AlertType type = AlertType.HighTemperature;
        const AlertSeverity severity = AlertSeverity.Warning;
        const string message = "High temperature alert: 40.0°C (threshold: 35.0°C)";

        // Act
        var result = WeatherAlert.Create(type, severity, message);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Type.Should().Be(AlertType.HighTemperature);
            result.Value.Severity.Should().Be(AlertSeverity.Warning);
            result.Value.Message.Should().Be(message);
        }
    }

    [Theory]
    [InlineData(AlertType.HighTemperature)]
    [InlineData(AlertType.LowTemperature)]
    [InlineData(AlertType.HighWind)]
    public void Create_WithDifferentTypes_ReturnsCorrectType(AlertType expectedType)
    {
        // Act
        var result = WeatherAlert.Create(expectedType, AlertSeverity.Info, "Test message");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Type.Should().Be(expectedType);
        }
    }

    [Theory]
    [InlineData(AlertSeverity.Info)]
    [InlineData(AlertSeverity.Warning)]
    [InlineData(AlertSeverity.Critical)]
    public void Create_WithDifferentSeverities_ReturnsCorrectSeverity(AlertSeverity expectedSeverity)
    {
        // Act
        var result = WeatherAlert.Create(AlertType.HighTemperature, expectedSeverity, "Test message");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Severity.Should().Be(expectedSeverity);
        }
    }

    [Fact]
    public void Create_WhenMaxLengthMessage_ReturnsSuccess()
    {
        // Arrange
        var message = new string('a', WeatherAlert.MaxMessageLength);

        // Act
        var result = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Message.Should().Be(message);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_WhenMessageEmpty_ReturnsValidationError(string? message)
    {
        // Act
        var result = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Alert.MessageRequired");
        }
    }

    [Fact]
    public void Create_WhenMessageTooLong_ReturnsValidationError()
    {
        // Arrange
        var message = new string('a', WeatherAlert.MaxMessageLength + 1);

        // Act
        var result = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            var validationError = result.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Alert.MessageTooLong");
        }
    }

    [Fact]
    public void Create_WhenMessageHasLeadingAndTrailingSpaces_TrimsThem()
    {
        // Arrange
        const string message = "  Weather alert  ";

        // Act
        var result = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Message.Should().Be("Weather alert");
        }
    }
}
