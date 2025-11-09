using System.ComponentModel.DataAnnotations;

namespace DotNetAtlas.OutboxRelay.WorkerService.Observability.HealthChecks;

/// <summary>
/// Configuration options for the OutboxRelay health check.
/// </summary>
public sealed class OutboxRelayHealthCheckOptions : IValidatableObject
{
    public const string Section = "OutboxRelayHealthCheck";

    /// <summary>
    /// How long to wait during startup before considering the service unhealthy if it hasn't executed.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:01:00", "00:30:00")]
    public required TimeSpan StartupGracePeriod { get; set; }

    /// <summary>
    /// Multiplier for the polling interval to determine the degraded threshold.
    /// Must be greater than 1.0 to be meaningful.
    /// </summary>
    [Required]
    [Range(1.1, 10.0)]
    public required double DegradedThresholdMultiplier { get; set; }

    /// <summary>
    /// Multiplier for the polling interval to determine an unhealthy threshold.
    /// Must be greater than DegradedThresholdMultiplier.
    /// </summary>
    [Required]
    [Range(1.2, 20.0)]
    public required double UnhealthyThresholdMultiplier { get; set; }

    /// <summary>
    /// Minimum degraded threshold (floor value). Prevents thresholds from being too short for fast polling.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public required TimeSpan MinimumDegradedThreshold { get; set; }

    /// <summary>
    /// Minimum unhealthy threshold (floor value). Prevents thresholds from being too short for fast polling.
    /// Must be greater than the MinimumDegradedThreshold.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:10", "00:30:00")]
    public required TimeSpan MinimumUnhealthyThreshold { get; set; }

    /// <summary>
    /// When the service started (set by the health check registration).
    /// </summary>
    public DateTimeOffset ServiceStartTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Custom validation to ensure logical consistency between thresholds.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (UnhealthyThresholdMultiplier <= DegradedThresholdMultiplier)
        {
            results.Add(new ValidationResult(
                "UnhealthyThresholdMultiplier must be greater than DegradedThresholdMultiplier.",
                [nameof(UnhealthyThresholdMultiplier)]));
        }

        if (MinimumUnhealthyThreshold <= MinimumDegradedThreshold)
        {
            results.Add(new ValidationResult(
                "MinimumUnhealthyThreshold must be greater than MinimumDegradedThreshold.",
                [nameof(MinimumUnhealthyThreshold)]));
        }

        return results;
    }
}
