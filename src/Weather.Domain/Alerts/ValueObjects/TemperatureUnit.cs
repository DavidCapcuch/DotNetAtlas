using System.Diagnostics;
using System.Globalization;
using Ardalis.SmartEnum;

namespace Weather.Domain.Alerts.ValueObjects;

/// <summary>
/// Smart enum representing temperature unit preference for subscribers.
/// Encapsulates unit identity and conversion logic using a hub-and-spoke pattern through Celsius.
/// </summary>
public sealed class TemperatureUnit : SmartEnum<TemperatureUnit>
{
    // Conversion constants
    private const double CelsiusToFahrenheitMultiplier = 9.0 / 5.0; // 1.8
    private const double FahrenheitOffset = 32.0;
    private const double CelsiusToKelvinOffset = 273.15;

    public static readonly TemperatureUnit Celsius = new(nameof(Celsius), 0, "°C");
    public static readonly TemperatureUnit Fahrenheit = new(nameof(Fahrenheit), 1, "°F");
    public static readonly TemperatureUnit Kelvin = new(nameof(Kelvin), 2, "K");

    /// <summary>
    /// The symbol used to display this temperature unit.
    /// </summary>
    public string Symbol { get; }

    private TemperatureUnit(string name, int value, string symbol)
        : base(name, value)
    {
        Symbol = symbol;
    }

    /// <summary>
    /// Converts a temperature from Celsius to this unit.
    /// </summary>
    /// <param name="celsius">The temperature in Celsius.</param>
    /// <returns>The temperature converted to this unit.</returns>
    /// <remarks>
    /// Example: <c>TemperatureUnit.Fahrenheit.FromCelsius(0.0)</c> returns <c>32.0</c>.
    /// </remarks>
    public double FromCelsius(double celsius) => Value switch
    {
        0 => celsius, // Celsius
        1 => (celsius * CelsiusToFahrenheitMultiplier) + FahrenheitOffset, // Fahrenheit
        2 => celsius + CelsiusToKelvinOffset, // Kelvin
        _ => throw new UnreachableException($"Unknown temperature unit: {Name}")
    };

    /// <summary>
    /// Converts a temperature from this unit to Celsius.
    /// </summary>
    /// <param name="value">The temperature value in this unit.</param>
    /// <returns>The temperature converted to Celsius.</returns>
    /// <remarks>
    /// Example: <c>TemperatureUnit.Fahrenheit.ToCelsius(32.0)</c> returns <c>0.0</c>.
    /// </remarks>
    public double ToCelsius(double value) => Value switch
    {
        0 => value, // Celsius
        1 => (value - FahrenheitOffset) / CelsiusToFahrenheitMultiplier, // Fahrenheit
        2 => value - CelsiusToKelvinOffset, // Kelvin
        _ => throw new UnreachableException($"Unknown temperature unit: {Name}")
    };

    /// <summary>
    /// Converts a temperature value from this unit to the target unit.
    /// </summary>
    /// <param name="value">The temperature value in this unit.</param>
    /// <param name="targetUnit">The unit to convert to.</param>
    /// <returns>The temperature value in the target unit.</returns>
    /// <remarks>
    /// <para>Supports conversion between any two temperature units using Celsius as an intermediate.</para>
    /// <para>Examples:</para>
    /// <list type="bullet">
    /// <item><c>TemperatureUnit.Celsius.ConvertTo(100, TemperatureUnit.Fahrenheit)</c> returns <c>212.0</c></item>
    /// <item><c>TemperatureUnit.Fahrenheit.ConvertTo(32, TemperatureUnit.Kelvin)</c> returns <c>273.15</c></item>
    /// <item><c>TemperatureUnit.Kelvin.ConvertTo(273.15, TemperatureUnit.Fahrenheit)</c> returns <c>32.0</c></item>
    /// </list>
    /// </remarks>
    public double ConvertTo(double value, TemperatureUnit targetUnit)
    {
        // Hub-and-spoke: this → Celsius → target
        var celsius = ToCelsius(value);
        return targetUnit.FromCelsius(celsius);
    }

    /// <summary>
    /// Formats a temperature from Celsius to this unit with its symbol.
    /// </summary>
    /// <param name="celsius">The temperature in Celsius.</param>
    /// <param name="decimals">Number of decimal places (default 1).</param>
    /// <returns>A formatted string like "32.0°F".</returns>
    public string FormatFromCelsius(double celsius, int decimals = 1)
        => FromCelsius(celsius).ToString($"F{decimals}", CultureInfo.InvariantCulture) + Symbol;
}
