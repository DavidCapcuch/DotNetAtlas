using System.Globalization;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing a temperature value.
/// Stores temperature internally in Celsius (canonical unit for DB persistence) but is unit-agnostic in its API.
/// Use <see cref="In"/> to get the value in any unit, and comparison operators for unit-safe comparisons.
/// </summary>
public sealed record Temperature : ValueObject
{
    /// <summary>
    /// Absolute zero in Celsius - the physical minimum temperature.
    /// </summary>
    private const double AbsoluteZeroCelsius = -273.15;

    /// <summary>
    /// Internal storage in Celsius (canonical unit for DB persistence).
    /// Use <see cref="In"/> method to get value in specific unit.
    /// </summary>
    internal double Celsius { get; private init; }

    private Temperature()
    {
    }

    /// <summary>
    /// Creates a temperature from a value in the specified unit.
    /// </summary>
    /// <param name="value">The temperature value.</param>
    /// <param name="unit">The unit of the temperature value.</param>
    /// <returns>A result containing the Temperature or validation errors.</returns>
    public static Result<Temperature> From(double value, TemperatureUnit unit)
    {
        var celsiusValue = unit.ToCelsius(value);

        if (celsiusValue < AbsoluteZeroCelsius)
        {
            return Result.Fail(TemperatureErrors.InvalidTemperature(value, unit.Symbol));
        }

        return new Temperature
        {
            Celsius = celsiusValue
        };
    }

    /// <summary>
    /// Creates a temperature from a Celsius value.
    /// </summary>
    /// <param name="celsius">The temperature in Celsius.</param>
    /// <returns>A result containing the Temperature or validation errors.</returns>
    public static Result<Temperature> FromCelsius(double celsius) => From(celsius, TemperatureUnit.Celsius);

    /// <summary>
    /// Creates a temperature from a Fahrenheit value.
    /// </summary>
    /// <param name="fahrenheit">The temperature in Fahrenheit.</param>
    /// <returns>A result containing the Temperature or validation errors.</returns>
    public static Result<Temperature> FromFahrenheit(double fahrenheit) => From(fahrenheit, TemperatureUnit.Fahrenheit);

    /// <summary>
    /// Creates a temperature from a Kelvin value.
    /// </summary>
    /// <param name="kelvin">The temperature in Kelvin.</param>
    /// <returns>A result containing the Temperature or validation errors.</returns>
    public static Result<Temperature> FromKelvin(double kelvin) => From(kelvin, TemperatureUnit.Kelvin);

    /// <summary>
    /// Gets the temperature value in the specified unit.
    /// </summary>
    /// <param name="unit">The unit to convert to.</param>
    /// <returns>The temperature value in the specified unit.</returns>
    public double In(TemperatureUnit unit) => unit.FromCelsius(Celsius);

    /// <summary>
    /// Formats the temperature in the specified unit with its symbol.
    /// Uses invariant culture for consistent formatting across locales.
    /// </summary>
    /// <param name="unit">The unit to format in.</param>
    /// <param name="decimals">Number of decimal places (default 1).</param>
    /// <returns>A formatted string like "32.0°F".</returns>
    public string Format(TemperatureUnit unit, int decimals = 1)
        => $"{In(unit).ToString($"F{decimals}", CultureInfo.InvariantCulture)}{unit.Symbol}";

    /// <summary>
    /// Returns the difference between this temperature and another, in the specified unit.
    /// </summary>
    /// <param name="other">The temperature to subtract.</param>
    /// <param name="unit">The unit for the result.</param>
    /// <returns>The difference in the specified unit.</returns>
    public double DifferenceIn(Temperature other, TemperatureUnit unit)
        => In(unit) - other.In(unit);

    public override string ToString() => Format(TemperatureUnit.Celsius);

    /// <summary>
    /// Compares two temperatures for equality (unit-agnostic).
    /// </summary>
    public static bool operator >(Temperature left, Temperature right)
        => left.Celsius > right.Celsius;

    /// <summary>
    /// Compares two temperatures (unit-agnostic).
    /// </summary>
    public static bool operator <(Temperature left, Temperature right)
        => left.Celsius < right.Celsius;

    /// <summary>
    /// Compares two temperatures (unit-agnostic).
    /// </summary>
    public static bool operator >=(Temperature left, Temperature right)
        => left.Celsius >= right.Celsius;

    /// <summary>
    /// Compares two temperatures (unit-agnostic).
    /// </summary>
    public static bool operator <=(Temperature left, Temperature right)
        => left.Celsius <= right.Celsius;
}
