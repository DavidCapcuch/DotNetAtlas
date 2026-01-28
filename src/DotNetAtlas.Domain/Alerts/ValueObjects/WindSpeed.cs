using System.Globalization;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing a wind speed value.
/// Stores wind speed internally in km/h (canonical unit for DB persistence) but is unit-agnostic in its API.
/// Use <see cref="In"/> to get the value in any unit, and comparison operators for unit-safe comparisons.
/// </summary>
public sealed record WindSpeed : ValueObject
{
    /// <summary>
    /// Internal storage in km/h (canonical unit for DB persistence).
    /// Use <see cref="In"/> method to get value in specific unit.
    /// </summary>
    internal double KilometersPerHour { get; private init; }

    private WindSpeed()
    {
    }

    /// <summary>
    /// Creates a wind speed from a value in the specified unit.
    /// </summary>
    /// <param name="value">The wind speed value.</param>
    /// <param name="unit">The unit of the wind speed value.</param>
    /// <returns>A result containing the WindSpeed or validation errors.</returns>
    public static Result<WindSpeed> From(double value, WindSpeedUnit unit)
    {
        var kmhValue = unit.ToKilometersPerHour(value);

        if (kmhValue < 0)
        {
            return Result.Fail(WindSpeedErrors.InvalidWindSpeed(value));
        }

        return new WindSpeed
        {
            KilometersPerHour = kmhValue
        };
    }

    /// <summary>
    /// Creates a wind speed from a kilometers per hour value.
    /// </summary>
    /// <param name="kmh">The wind speed in kilometers per hour.</param>
    /// <returns>A result containing the WindSpeed or validation errors.</returns>
    public static Result<WindSpeed> FromKilometersPerHour(double kmh) => From(kmh, WindSpeedUnit.KilometersPerHour);

    /// <summary>
    /// Creates a wind speed from a miles per hour value.
    /// </summary>
    /// <param name="mph">The wind speed in miles per hour.</param>
    /// <returns>A result containing the WindSpeed or validation errors.</returns>
    public static Result<WindSpeed> FromMilesPerHour(double mph) => From(mph, WindSpeedUnit.MilesPerHour);

    /// <summary>
    /// Gets the wind speed value in the specified unit.
    /// </summary>
    /// <param name="unit">The unit to convert to.</param>
    /// <returns>The wind speed value in the specified unit.</returns>
    public double In(WindSpeedUnit unit) => unit.FromKilometersPerHour(KilometersPerHour);

    /// <summary>
    /// Formats the wind speed in the specified unit with its symbol.
    /// Uses invariant culture for consistent formatting across locales.
    /// </summary>
    /// <param name="unit">The unit to format in.</param>
    /// <param name="decimals">Number of decimal places (default 1).</param>
    /// <returns>A formatted string like "50.0 mph".</returns>
    public string Format(WindSpeedUnit unit, int decimals = 1)
        => $"{In(unit).ToString($"F{decimals}", CultureInfo.InvariantCulture)} {unit.Symbol}";

    /// <summary>
    /// Returns the difference between this wind speed and another, in the specified unit.
    /// </summary>
    /// <param name="other">The wind speed to subtract.</param>
    /// <param name="unit">The unit for the result.</param>
    /// <returns>The difference in the specified unit.</returns>
    public double DifferenceIn(WindSpeed other, WindSpeedUnit unit)
        => In(unit) - other.In(unit);

    public override string ToString() => Format(WindSpeedUnit.KilometersPerHour);

    /// <summary>
    /// Compares two wind speeds (unit-agnostic).
    /// </summary>
    public static bool operator >(WindSpeed left, WindSpeed right)
        => left.KilometersPerHour > right.KilometersPerHour;

    /// <summary>
    /// Compares two wind speeds (unit-agnostic).
    /// </summary>
    public static bool operator <(WindSpeed left, WindSpeed right)
        => left.KilometersPerHour < right.KilometersPerHour;

    /// <summary>
    /// Compares two wind speeds (unit-agnostic).
    /// </summary>
    public static bool operator >=(WindSpeed left, WindSpeed right)
        => left.KilometersPerHour >= right.KilometersPerHour;

    /// <summary>
    /// Compares two wind speeds (unit-agnostic).
    /// </summary>
    public static bool operator <=(WindSpeed left, WindSpeed right)
        => left.KilometersPerHour <= right.KilometersPerHour;
}
