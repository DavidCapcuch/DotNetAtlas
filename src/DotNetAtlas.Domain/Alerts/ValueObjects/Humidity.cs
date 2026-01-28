using System.Globalization;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.SharedKernel.Base;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing relative humidity as a percentage.
/// Humidity only has one unit (percent), so the API is simpler than Temperature/WindSpeed.
/// </summary>
public sealed record Humidity : ValueObject
{
    /// <summary>
    /// Internal storage (percent value).
    /// Use <see cref="Value"/> property to get the percentage value.
    /// </summary>
    internal double Percent { get; private init; }

    /// <summary>
    /// Gets the humidity as a percentage value (0-100).
    /// </summary>
    public double Value => Percent;

    private Humidity()
    {
    }

    /// <summary>
    /// Creates a humidity value from a percentage.
    /// </summary>
    /// <param name="percent">The relative humidity percentage (0-100).</param>
    /// <returns>A result containing the Humidity or validation errors.</returns>
    public static Result<Humidity> FromPercent(double percent)
    {
        if (percent is < 0 or > 100)
        {
            return Result.Fail(HumidityErrors.InvalidHumidity(percent));
        }

        return new Humidity
        {
            Percent = percent
        };
    }

    /// <summary>
    /// Formats the humidity with its symbol.
    /// Uses invariant culture for consistent formatting across locales.
    /// </summary>
    /// <param name="decimals">Number of decimal places (default 1).</param>
    /// <returns>A formatted string like "75.0%".</returns>
    public string Format(int decimals = 1)
        => string.Format(CultureInfo.InvariantCulture, $"{{0:F{decimals}}}%", Percent);

    /// <summary>
    /// Returns the difference between this humidity and another.
    /// </summary>
    /// <param name="other">The humidity to subtract.</param>
    /// <returns>The difference in percentage points.</returns>
    public double Difference(Humidity other) => Percent - other.Percent;

    public override string ToString() => Format();

    /// <summary>
    /// Compares two humidity values.
    /// </summary>
    public static bool operator >(Humidity left, Humidity right)
        => left.Percent > right.Percent;

    /// <summary>
    /// Compares two humidity values.
    /// </summary>
    public static bool operator <(Humidity left, Humidity right)
        => left.Percent < right.Percent;

    /// <summary>
    /// Compares two humidity values.
    /// </summary>
    public static bool operator >=(Humidity left, Humidity right)
        => left.Percent >= right.Percent;

    /// <summary>
    /// Compares two humidity values.
    /// </summary>
    public static bool operator <=(Humidity left, Humidity right)
        => left.Percent <= right.Percent;
}
