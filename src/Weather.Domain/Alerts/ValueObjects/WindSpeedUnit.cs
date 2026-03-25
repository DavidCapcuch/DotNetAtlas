using System.Globalization;
using Ardalis.SmartEnum;

namespace Weather.Domain.Alerts.ValueObjects;

/// <summary>
/// Smart enum representing wind speed unit preference for subscribers.
/// Encapsulates unit identity and conversion logic.
/// </summary>
public sealed class WindSpeedUnit : SmartEnum<WindSpeedUnit>
{
    private const double KmhToMphFactor = 0.621371;

    public static readonly WindSpeedUnit KilometersPerHour = new(nameof(KilometersPerHour), 0, "km/h");
    public static readonly WindSpeedUnit MilesPerHour = new(nameof(MilesPerHour), 1, "mph");

    /// <summary>
    /// The symbol used to display this wind speed unit.
    /// </summary>
    public string Symbol { get; }

    private WindSpeedUnit(string name, int value, string symbol)
        : base(name, value)
    {
        Symbol = symbol;
    }

    /// <summary>
    /// Converts a wind speed from km/h to this unit.
    /// </summary>
    /// <param name="kmh">The wind speed in kilometers per hour.</param>
    /// <returns>The wind speed converted to this unit.</returns>
    public double FromKilometersPerHour(double kmh) => this switch
    {
        _ when this == KilometersPerHour => kmh,
        _ when this == MilesPerHour => kmh * KmhToMphFactor,
        _ => throw new NotSupportedException($"Wind speed unit '{Name}' is not supported.")
    };

    /// <summary>
    /// Converts a wind speed from this unit to km/h.
    /// </summary>
    /// <param name="value">The wind speed value in this unit.</param>
    /// <returns>The wind speed converted to kilometers per hour.</returns>
    public double ToKilometersPerHour(double value) => this switch
    {
        _ when this == KilometersPerHour => value,
        _ when this == MilesPerHour => value / KmhToMphFactor,
        _ => throw new NotSupportedException($"Wind speed unit '{Name}' is not supported.")
    };

    /// <summary>
    /// Formats a wind speed from km/h to this unit with its symbol.
    /// </summary>
    /// <param name="kmh">The wind speed in kilometers per hour.</param>
    /// <param name="decimals">Number of decimal places (default 1).</param>
    /// <returns>A formatted string like "50.0 mph".</returns>
    public string FormatFromKilometersPerHour(double kmh, int decimals = 1)
        => FromKilometersPerHour(kmh).ToString($"F{decimals}", CultureInfo.InvariantCulture) + " " + Symbol;
}
