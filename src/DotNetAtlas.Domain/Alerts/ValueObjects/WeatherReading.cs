using DotNetAtlas.SharedKernel.Base;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing a weather sensor reading at a point in time.
/// Composes Temperature, Humidity, and WindSpeed value objects.
/// </summary>
public sealed record WeatherReading : ValueObject
{
    /// <summary>
    /// Temperature reading.
    /// </summary>
    public Temperature Temperature { get; private init; } = null!;

    /// <summary>
    /// Relative humidity reading.
    /// </summary>
    public Humidity Humidity { get; private init; } = null!;

    /// <summary>
    /// Wind speed reading.
    /// </summary>
    public WindSpeed WindSpeed { get; private init; } = null!;

    /// <summary>
    /// UTC timestamp when the reading was recorded.
    /// </summary>
    public DateTimeOffset RecordedAtUtc { get; private init; }

    private WeatherReading()
    {
    }

    /// <summary>
    /// Creates a new weather reading from validated value objects.
    /// </summary>
    /// <param name="temperature">Temperature value object.</param>
    /// <param name="humidity">Humidity value object.</param>
    /// <param name="windSpeed">Wind speed value object.</param>
    /// <param name="recordedAtUtc">UTC timestamp when the reading was recorded.</param>
    /// <returns>A new WeatherReading instance.</returns>
    public static WeatherReading Create(
        Temperature temperature,
        Humidity humidity,
        WindSpeed windSpeed,
        DateTimeOffset recordedAtUtc)
    {
        return new WeatherReading
        {
            Temperature = temperature,
            Humidity = humidity,
            WindSpeed = windSpeed,
            RecordedAtUtc = recordedAtUtc
        };
    }
}
