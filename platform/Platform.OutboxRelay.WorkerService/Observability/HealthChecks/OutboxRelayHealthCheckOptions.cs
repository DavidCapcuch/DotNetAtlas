using System.ComponentModel.DataAnnotations;

namespace Platform.OutboxRelay.WorkerService.Observability.HealthChecks;

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
    /// How long since last successful execution before the service is considered degraded.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:05", "00:10:00")]
    public required TimeSpan DegradedThreshold { get; set; }

    /// <summary>
    /// How long since last successful execution before the service is considered unhealthy.
    /// Must be greater than DegradedThreshold.
    /// </summary>
    [Required]
    [Range(typeof(TimeSpan), "00:00:10", "00:30:00")]
    public required TimeSpan UnhealthyThreshold { get; set; }

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

        if (UnhealthyThreshold <= DegradedThreshold)
        {
            results.Add(new ValidationResult(
                "UnhealthyThreshold must be greater than DegradedThreshold.",
                [nameof(UnhealthyThreshold)]));
        }

        return results;
    }
}
