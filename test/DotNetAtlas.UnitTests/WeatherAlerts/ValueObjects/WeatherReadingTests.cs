using DotNetAtlas.Domain.Alerts.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace DotNetAtlas.UnitTests.WeatherAlerts.ValueObjects;

public class WeatherReadingTests
{
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    [Fact]
    public void Create_WhenValidInput_ReturnsWeatherReading()
    {
        // Arrange
        var recordedAtUtc = _fakeTimeProvider.GetUtcNow();
        var temperature = Temperature.FromCelsius(25.0).Value;
        var humidity = Humidity.FromPercent(50.0).Value;
        var windSpeed = WindSpeed.FromKilometersPerHour(15.0).Value;

        // Act
        var weatherReading = WeatherReading.Create(
            temperature,
            humidity,
            windSpeed,
            recordedAtUtc);

        // Assert
        using (new AssertionScope())
        {
            weatherReading.Should().NotBeNull();
            weatherReading.Temperature.In(TemperatureUnit.Celsius).Should().Be(25.0);
            weatherReading.Humidity.Value.Should().Be(50.0);
            weatherReading.WindSpeed.In(WindSpeedUnit.KilometersPerHour).Should().Be(15.0);
            weatherReading.RecordedAtUtc.Should().Be(recordedAtUtc);
        }
    }
}
