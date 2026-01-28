using Ardalis.Specification;
using DotNetAtlas.Domain.Alerts.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Domain.Alerts.Specifications.AlertConditions;

/// <summary>
/// Base specification for alert condition evaluation.
/// Inherits from Ardalis.Specification to provide both Query.Where() for database queries
/// and IsSatisfiedBy() for in-memory validation.
/// </summary>
public abstract class WeatherAlertSpec : Specification<WeatherReading>
{
    /// <summary>
    /// The type of alert this specification evaluates.
    /// </summary>
    public abstract AlertType AlertType { get; }

    /// <summary>
    /// Creates the alert condition when the specification is satisfied.
    /// </summary>
    /// <param name="reading">The weather reading that triggered the alert.</param>
    /// <returns>A Result containing the WeatherAlert with type, severity, and message.</returns>
    public abstract Result<WeatherAlert> CreateAlert(WeatherReading reading);
}
